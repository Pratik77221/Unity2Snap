import * as fs from 'LensStudio:FileSystem';
import * as AssetUtils from 'LensStudio:AssetUtils.js';

const IMPORT_ROOT_NAME = 'Imported_Unity_Scene';
const IMPORT_ASSET_DIR = 'Unity2Snap';
const GENERATED_PRIMITIVE_DIR = 'assets/generated_primitives';
const GENERATED_MATERIAL_DIR = 'Unity2Snap/Materials';
const GENERATED_PRIMITIVE_ASSET_VERSION = 'v3';

export function readManifest(exportDir) {
    const normalizedDir = normalizePath(exportDir);
    if (!normalizedDir) {
        throw new Error('Export folder is empty.');
    }

    const manifestPath = new Editor.Path(normalizedDir).appended('scene.usls.json');
    if (!fs.exists(manifestPath) || !fs.isFile(manifestPath)) {
        throw new Error('scene.usls.json was not found at ' + manifestPath.toString());
    }

    const json = fs.readFile(manifestPath);
    const manifest = JSON.parse(json);
    validateManifest(manifest);
    return { exportDir: normalizedDir, manifest: manifest };
}

export function createAnalyzeSummary(manifest) {
    const lines = [];
    lines.push('Scene: ' + safe(manifest.exporter && manifest.exporter.sceneName, 'unknown'));
    lines.push('Objects: ' + safeNumber(manifest.stats && manifest.stats.objectCount));
    lines.push('Meshes: ' + safeNumber(manifest.stats && manifest.stats.meshObjectCount));
    lines.push('Primitives: ' + safeNumber(manifest.stats && manifest.stats.primitiveObjectCount));
    lines.push('Lights: ' + safeNumber(manifest.stats && manifest.stats.lightCount));
    lines.push('Camera hints: ' + safeNumber(manifest.stats && manifest.stats.cameraHintCount));
    lines.push('Colliders: ' + safeNumber(manifest.stats && manifest.stats.colliderCount));
    lines.push('Triangles: ' + safeNumber(manifest.stats && manifest.stats.totalTriangles));
    lines.push('Warnings: ' + countWarnings(manifest));
    lines.push('Importable asset files: ' + countImportableAssets(manifest));
    lines.push('Material definitions: ' + countMaterialDefinitions(manifest));
    lines.push('Player spawns / XR rigs: ' + countPlayerSpawns(manifest));
    lines.push('Repeated source model risks: ' + countRepeatedSourceModelRisks(manifest));
    return lines.join('\n');
}

export async function importManifest(pluginSystem, exportDir, manifest, status) {
    const model = pluginSystem.findInterface(Editor.Model.IModel);
    const project = model.project;
    const scene = project.scene;
    const assetManager = project.assetManager;

    status('Importing assets...');
    const importedAssets = await importUslsAssets(assetManager, exportDir, manifest);
    const primitiveAssets = await importPrimitiveAssets(assetManager, exportDir, manifest);
    const materialAssets = await createUslsMaterials(assetManager, manifest, importedAssets);

    status('Creating scene hierarchy...');
    return await importUslsObjects(scene, assetManager, manifest, importedAssets, primitiveAssets, materialAssets);
}

export function errorToString(error) {
    if (!error) {
        return 'unknown error';
    }

    if (error.stack) {
        return error.stack;
    }

    if (error.message) {
        return error.message;
    }

    return String(error);
}

async function importUslsAssets(assetManager, exportDir, manifest) {
    const imported = {};
    const assets = manifest.assets || [];

    for (let i = 0; i < assets.length; i++) {
        const asset = assets[i];
        if (!asset || !asset.id || !asset.path) {
            continue;
        }

        const absolutePath = pathFromRelative(exportDir, asset.path);
        if (!fs.exists(absolutePath) || !fs.isFile(absolutePath)) {
            console.warn('[Unity2Snap] Missing asset file: ' + absolutePath.toString());
            continue;
        }

        try {
            const existing = assetManager.findImportedCopy(absolutePath, IMPORT_ASSET_DIR);
            if (existing && !Editor.isNull(existing)) {
                const existingAsset = assetFromMeta(existing, assetManager);
                if (existingAsset && !Editor.isNull(existingAsset)) {
                    imported[asset.id] = existingAsset;
                    continue;
                }
            }
        } catch (e) {
            // Older Lens Studio builds can throw when no imported copy exists.
        }

        try {
            const importResult = await assetManager.importExternalFileAsync(
                absolutePath,
                IMPORT_ASSET_DIR,
                Editor.Model.ResultType.Packed
            );

            imported[asset.id] = importResult.primary;
        } catch (error) {
            console.warn('[Unity2Snap] Failed to import asset ' + asset.path + ': ' + errorToString(error));
        }
    }

    return imported;
}

async function importUslsObjects(scene, assetManager, manifest, importedAssets, primitiveAssets, materialAssets) {
    const result = {
        createdObjects: 0,
        removedPreviousImports: 0,
        importedAssetFiles: Object.keys(importedAssets).length,
        generatedPrimitiveAssets: Object.keys(primitiveAssets).length,
        generatedMaterials: Object.keys(materialAssets).length,
        instantiatedMeshes: 0,
        instantiatedPrimitives: 0,
        assignedMaterials: 0,
        createdLights: 0,
        createdColliders: 0,
        notes: []
    };

    result.removedPreviousImports = removeExistingImportedRoots(scene);
    const root = scene.createSceneObject(IMPORT_ROOT_NAME);
    setLocalTransform(root, identityTransform());

    const objectById = {};
    const objects = manifest.objects || [];
    const sourceById = buildSourceById(objects);
    const assetPolicy = buildAssetPolicy(manifest);

    for (let i = 0; i < objects.length; i++) {
        const source = objects[i];
        const obj = scene.createSceneObject(safe(source.name, source.type || 'USLS Object'));
        obj.enabled = source.enabled !== false;
        objectById[source.id] = obj;
        result.createdObjects++;
    }

    for (let i = 0; i < objects.length; i++) {
        const source = objects[i];
        const obj = objectById[source.id];
        const parent = source.parentId ? objectById[source.parentId] : root;
        if (parent) {
            obj.setParent(parent);
        } else {
            obj.setParent(root);
        }
        setLocalTransform(obj, source.transform);
    }

    const playerSpawn = findPlayerSpawn(objects);
    if (playerSpawn) {
        applyPlayerSpawnOffset(root, playerSpawn, objectById, sourceById);
        result.notes.push('Applied player_spawn offset from ' + playerSpawn.path);
    }

    for (let i = 0; i < objects.length; i++) {
        const source = objects[i];
        const obj = objectById[source.id];
        if (!obj) {
            continue;
        }

        if (hasLightPayload(source.light)) {
            if (createLightComponent(obj, source.light)) {
                result.createdLights++;
            }
        }

        if (hasPrimitivePayload(source)) {
            const primitiveImport = await instantiatePrimitiveAsset(assetManager, obj, source, primitiveAssets);
            if (primitiveImport.instantiated) {
                result.instantiatedPrimitives++;
                result.assignedMaterials += numberOr(primitiveImport.assignedMaterials, 0);
            } else {
                addImportNote(obj, primitiveImport.note || 'Primitive asset not instantiated.');
                result.notes.push('Primitive placeholder: ' + source.path);
            }
        } else if (hasMeshPayload(source.mesh)) {
            const meshImport = await instantiateMeshAsset(assetManager, obj, source, importedAssets, assetPolicy, materialAssets);
            if (meshImport.instantiated) {
                result.instantiatedMeshes++;
                result.assignedMaterials += numberOr(meshImport.assignedMaterials, 0);
            } else {
                addImportNote(obj, meshImport.note || ('Mesh asset not instantiated. assetId=' + safe(source.mesh.assetId, 'none')));
                result.notes.push('Mesh placeholder: ' + source.path);
            }
        }

        if (source.type === 'player_spawn') {
            addImportNote(obj, 'Player/XR rig marker for Spectacles. Imported scene root is offset so this authored user origin aligns to Lens origin.');
        }

        if (hasCameraPayload(source)) {
            decorateCameraHint(obj, source.camera);
        }

        if (hasColliderPayload(source.collider)) {
            const colliderImport = createColliderComponent(obj, source.collider);
            if (colliderImport.created) {
                result.createdColliders++;
            } else {
                addImportNote(obj, colliderImport.note || 'Collider metadata only. Component creation failed.');
                result.notes.push('Collider placeholder: ' + source.path);
            }
        }
    }

    attachWarningSummary(scene, root, manifest, result);
    return result;
}

function removeExistingImportedRoots(scene) {
    const roots = scene.rootSceneObjects || [];
    const staleRoots = [];

    for (let i = 0; i < roots.length; i++) {
        const root = roots[i];
        if (root && !Editor.isNull(root) && isImportedRootName(root.name)) {
            staleRoots.push(root);
        }
    }

    let removed = 0;
    for (let i = 0; i < staleRoots.length; i++) {
        try {
            staleRoots[i].destroy();
            removed++;
        } catch (error) {
            console.warn('[Unity2Snap] Previous import cleanup failed for ' + staleRoots[i].name + ': ' + errorToString(error));
        }
    }

    if (removed > 0) {
        console.log('[Unity2Snap] Removed previous imported scene roots: ' + removed);
    }

    return removed;
}

function isImportedRootName(name) {
    const value = safe(name, '');
    return value === IMPORT_ROOT_NAME || /^Imported_Unity_Scene\s*\(\d+\)$/.test(value);
}

async function createUslsMaterials(assetManager, manifest, importedAssets) {
    const materialRefs = collectMeshMaterialRefs(manifest);
    const materialAssets = {};

    for (let i = 0; i < materialRefs.length; i++) {
        const materialRef = materialRefs[i];
        const materialKey = materialRef.assetId || createMaterialKey(materialRef);
        if (!materialKey || materialAssets[materialKey]) {
            continue;
        }

        try {
            const material = await findOrCreateUslsMaterial(assetManager, materialRef);
            configureUslsMaterial(material.material, material.passInfo, materialRef, importedAssets);
            materialAssets[materialKey] = material.material;
        } catch (error) {
            console.warn('[Unity2Snap] Failed to create material ' + safe(materialRef.name, materialKey) + ': ' + errorToString(error));
        }
    }

    return materialAssets;
}

function collectMeshMaterialRefs(manifest) {
    const refs = [];
    const seen = {};
    const objects = manifest.objects || [];

    for (let i = 0; i < objects.length; i++) {
        const object = objects[i];
        if (hasPrimitivePayload(object) || !hasMeshPayload(object && object.mesh)) {
            continue;
        }

        const objectRefs = getMaterialRefsForSource(object);
        for (let j = 0; j < objectRefs.length; j++) {
            const materialRef = objectRefs[j];
            const key = materialRef.assetId || createMaterialKey(materialRef);
            if (key && !seen[key]) {
                seen[key] = true;
                refs.push(materialRef);
            }
        }
    }

    return refs;
}

function collectMaterialRefs(manifest) {
    const refs = [];
    const seen = {};
    const objects = manifest.objects || [];

    for (let i = 0; i < objects.length; i++) {
        const object = objects[i];
        const objectRefs = getMaterialRefsForSource(object);
        for (let j = 0; j < objectRefs.length; j++) {
            const materialRef = objectRefs[j];
            const key = materialRef.assetId || createMaterialKey(materialRef);
            if (key && !seen[key]) {
                seen[key] = true;
                refs.push(materialRef);
            }
        }
    }

    return refs;
}

async function findOrCreateUslsMaterial(assetManager, materialRef) {
    const materialName = createLensMaterialName(materialRef);
    const existing = findAssetByName(assetManager, materialName, 'Material');
    if (existing) {
        return { material: existing, passInfo: firstPassInfo(existing) };
    }

    try {
        const created = await AssetUtils.createMaterialFromGraph(
            assetManager,
            AssetUtils.ShaderGraphType.ShaderGraphUnlit,
            GENERATED_MATERIAL_DIR,
            materialName,
            GENERATED_MATERIAL_DIR + '/Graphs'
        );

        return {
            material: created.material,
            passInfo: created.passInfo
        };
    } catch (error) {
        const material = assetManager.createNativeAsset('Material', materialName, GENERATED_MATERIAL_DIR);
        return { material: material, passInfo: firstPassInfo(material) };
    }
}

function configureUslsMaterial(material, passInfo, materialRef, importedAssets) {
    const color = colorToVec4(materialRef.baseColor, [1, 1, 1, 1]);
    const texture = materialRef.albedoTextureAssetId ? importedAssets[materialRef.albedoTextureAssetId] : null;
    const normalTexture = materialRef.normalTextureAssetId ? importedAssets[materialRef.normalTextureAssetId] : null;
    const metallicTexture = materialRef.metallicTextureAssetId ? importedAssets[materialRef.metallicTextureAssetId] : null;
    const passes = collectMaterialPasses(material, passInfo);

    for (let i = 0; i < passes.length; i++) {
        configurePass(passes[i], materialRef, color, texture, normalTexture, metallicTexture);
    }

    configureRuntimeMaterial(material, materialRef, importedAssets);
}

function configurePass(pass, materialRef, color, texture, normalTexture, metallicTexture) {
    if (!pass) {
        return;
    }

    setPassValue(pass, 'baseColor', color);
    setPassValue(pass, 'color', color);
    setPassValue(pass, 'diffuseColor', color);
    setPassValue(pass, 'albedoColor', color);
    setPassValue(pass, 'baseColorFactor', color);
    setPassValue(pass, 'tint', color);
    setPassValue(pass, 'tintColor', color);
    setPassValue(pass, 'mainColor', color);

    if (texture && !Editor.isNull(texture)) {
        setPassValue(pass, 'baseTex', texture);
        setPassValue(pass, 'baseTexture', texture);
        setPassValue(pass, 'albedoTex', texture);
        setPassValue(pass, 'diffuseTex', texture);
        setPassValue(pass, 'mainTex', texture);
    }

    if (normalTexture && !Editor.isNull(normalTexture)) {
        setPassValue(pass, 'normalTex', normalTexture);
        setPassValue(pass, 'normalTexture', normalTexture);
    }

    if (metallicTexture && !Editor.isNull(metallicTexture)) {
        setPassValue(pass, 'metallicTex', metallicTexture);
        setPassValue(pass, 'metallicTexture', metallicTexture);
    }

    setPassValue(pass, 'metallic', numberOr(materialRef.metallic, 0));
    setPassValue(pass, 'roughness', numberOr(materialRef.roughness, 0.5));
    setPassValue(pass, 'twoSided', true);

    if (materialRef.transparent) {
        setPassValue(pass, 'blendMode', 0);
    }
}

function collectMaterialPasses(material, passInfo) {
    const passes = [];

    if (material) {
        try {
            if (material.mainPass) {
                passes.push(material.mainPass);
            }
        } catch (error) {
            // Keep collecting alternate pass surfaces.
        }

        try {
            const passInfos = material.passInfos || [];
            for (let i = 0; i < passInfos.length; i++) {
                addPassInfoPass(passes, passInfos[i]);
            }
        } catch (error) {
            // Some material asset versions hide passInfos.
        }

        try {
            if (typeof material.getPassCount === 'function' && typeof material.getPass === 'function') {
                const count = material.getPassCount();
                for (let i = 0; i < count; i++) {
                    passes.push(material.getPass(i));
                }
            }
        } catch (error) {
            // Runtime material API is not always exposed in editor plugins.
        }
    }

    addPassInfoPass(passes, passInfo);
    return passes;
}

function addPassInfoPass(passes, passInfo) {
    if (!passInfo) {
        return;
    }

    passes.push(passInfo);

    if (passInfo.pass) {
        passes.push(passInfo.pass);
    }

    if (passInfo.materialPass) {
        passes.push(passInfo.materialPass);
    }
}

function setPassValue(pass, propertyName, value) {
    try {
        pass[propertyName] = value;
        return true;
    } catch (error) {
        // Graphs differ by version; missing properties are expected.
        return false;
    }
}

function configureRuntimeMaterial(material, materialRef, importedAssets) {
    if (!material || Editor.isNull(material)) {
        return;
    }

    const color = colorToVec4(materialRef.baseColor, [1, 1, 1, 1]);
    const texture = materialRef.albedoTextureAssetId ? importedAssets[materialRef.albedoTextureAssetId] : null;

    try {
        if (material.mainPass) {
            configurePass(material.mainPass, materialRef, color, texture, null, null);
        }
    } catch (error) {
        // Keep trying alternate material pass surfaces.
    }

    try {
        if (typeof material.getPassCount === 'function' && typeof material.getPass === 'function') {
            const count = material.getPassCount();
            for (let i = 0; i < count; i++) {
                configurePass(material.getPass(i), materialRef, color, texture, null, null);
            }
        }
    } catch (error) {
        // Editor material assets often expose passInfos instead of runtime Pass objects.
    }
}

function findAssetByName(assetManager, name, typeName) {
    const assets = assetManager.assets || [];
    for (let i = 0; i < assets.length; i++) {
        const asset = assets[i];
        if (!asset || Editor.isNull(asset)) {
            continue;
        }

        if (asset.name === name && (!typeName || asset.type === typeName || asset.getTypeName && asset.getTypeName() === typeName)) {
            return asset;
        }
    }

    return null;
}

function firstPassInfo(material) {
    try {
        const passInfos = material.passInfos || [];
        return passInfos.length > 0 ? passInfos[0] : null;
    } catch (error) {
        return null;
    }
}

function getMaterialRefsForSource(source) {
    const refs = [];
    if (!source) {
        return refs;
    }

    const materials = source.materials || [];
    for (let i = 0; i < materials.length; i++) {
        if (isMaterialRefValid(materials[i])) {
            refs.push(materials[i]);
        }
    }

    if (refs.length === 0 && isMaterialRefValid(source.material)) {
        refs.push(source.material);
    }

    return refs;
}

function isMaterialRefValid(materialRef) {
    if (!materialRef) {
        return false;
    }

    return !!materialRef.assetId ||
        !!materialRef.name ||
        isMeaningfulColor(materialRef.baseColor) ||
        !!materialRef.albedoTextureAssetId;
}

function createLensMaterialName(materialRef) {
    const baseName = sanitizeAssetName(safe(materialRef.name, 'Material'));
    const id = safe(materialRef.assetId, createMaterialKey(materialRef));
    const suffix = id.length > 8 ? id.substring(id.length - 8) : id;
    return 'USLS_' + baseName + '_' + suffix;
}

function createMaterialKey(materialRef) {
    if (!materialRef) {
        return '';
    }

    return [
        safe(materialRef.name, 'mat'),
        colorKey(materialRef.baseColor),
        safe(materialRef.albedoTextureAssetId, '')
    ].join('_');
}

function colorKey(color) {
    if (!isColorArray(color)) {
        return 'color';
    }

    return [
        numberOr(color[0], 1).toFixed(3),
        numberOr(color[1], 1).toFixed(3),
        numberOr(color[2], 1).toFixed(3),
        numberOr(color[3], 1).toFixed(3)
    ].join('_');
}

function sanitizeAssetName(value) {
    return String(value || 'Material').replace(/[^A-Za-z0-9_]+/g, '_').replace(/^_+|_+$/g, '') || 'Material';
}

async function importPrimitiveAssets(assetManager, exportDir, manifest) {
    const variants = collectPrimitiveVariants(manifest);
    const imported = {};
    const variantKeys = Object.keys(variants);
    if (variantKeys.length === 0) {
        return imported;
    }

    const folderPath = pathFromRelative(exportDir, GENERATED_PRIMITIVE_DIR);
    ensureDirectory(folderPath);

    for (let i = 0; i < variantKeys.length; i++) {
        const variant = variants[variantKeys[i]];
        const objText = createPrimitiveObj(variant.shape, variant.materialName, variant.mtlFileName);
        if (!objText) {
            continue;
        }

        const filePath = folderPath.appended(variant.objFileName);
        fs.writeFile(filePath, objText);
        fs.writeFile(folderPath.appended(variant.mtlFileName), createPrimitiveMtl(variant));

        const destinationDir = IMPORT_ASSET_DIR + '/GeneratedPrimitives';
        const existingAsset = findImportedAsset(assetManager, filePath, destinationDir);
        if (existingAsset && !Editor.isNull(existingAsset)) {
            imported[variant.key] = existingAsset;
            continue;
        }

        try {
            const importResult = await assetManager.importExternalFileAsync(
                filePath,
                destinationDir,
                Editor.Model.ResultType.Packed
            );

            imported[variant.key] = importResult.primary;
        } catch (error) {
            console.warn('[Unity2Snap] Failed to import generated primitive ' + variant.key + ': ' + errorToString(error));
        }
    }

    return imported;
}

function findImportedAsset(assetManager, absolutePath, destinationDir) {
    try {
        const existing = assetManager.findImportedCopy(absolutePath, destinationDir);
        if (existing && !Editor.isNull(existing)) {
            return assetFromMeta(existing, assetManager);
        }
    } catch (error) {
        // Lens Studio throws on some builds when no imported copy exists.
    }

    return null;
}

async function instantiatePrimitiveAsset(assetManager, parentObject, source, primitiveAssets) {
    const shape = source.primitive && source.primitive.shape ? source.primitive.shape : '';
    if (!shape) {
        return {
            instantiated: false,
            note: 'Primitive has no shape.'
        };
    }

    const assetKey = primitiveAssetKey(source);
    const asset = primitiveAssets[assetKey] || primitiveAssets[shape];
    if (!asset || Editor.isNull(asset)) {
        return {
            instantiated: false,
            note: 'Generated primitive asset was not imported for key=' + assetKey
        };
    }

    try {
        const instances = await instantiateAssetUnderParent(assetManager, asset, parentObject);
        normalizeInstantiatedChildren(instances, parentObject);
        const assignedMaterials = assignImportedPrimitiveMaterialToInstances(assetManager, instances, parentObject, source, shape);
        return {
            instantiated: instances && instances.length > 0,
            assignedMaterials: assignedMaterials,
            note: 'Lens Studio did not return an instantiated primitive object.'
        };
    } catch (error) {
        console.warn('[Unity2Snap] Primitive instantiate failed for ' + source.path + ': ' + errorToString(error));
        return {
            instantiated: false,
            note: 'Generated primitive import succeeded but instantiation failed: ' + errorToString(error)
        };
    }
}

async function instantiateMeshAsset(assetManager, parentObject, source, importedAssets, assetPolicy, materialAssets) {
    const meshRef = source.mesh;
    if (!meshRef || !meshRef.assetId) {
        return {
            instantiated: false,
            note: 'Mesh asset not instantiated because this object has no mesh asset id.'
        };
    }

    if (assetPolicy.repeatedSourceAssetIds[meshRef.assetId]) {
        return {
            instantiated: false,
            note: 'Skipped automatic mesh instantiation because this asset comes from a repeated source model. Use per-object GLB/FBX export before visual import.'
        };
    }

    const asset = importedAssets[meshRef.assetId];
    if (!asset || Editor.isNull(asset)) {
        return {
            instantiated: false,
            note: 'Mesh asset was not imported. assetId=' + meshRef.assetId
        };
    }

    try {
        const instances = await instantiateAssetUnderParent(assetManager, asset, parentObject);
        normalizeInstantiatedChildren(instances, parentObject);
        const assignedMaterials = applyUslsMaterialsToInstances(instances, parentObject, source, materialAssets, importedAssets);
        return {
            instantiated: instances && instances.length > 0,
            assignedMaterials: assignedMaterials,
            note: 'Lens Studio did not return an instantiated object for this asset.'
        };
    } catch (error) {
        console.warn('[Unity2Snap] instantiate failed for ' + source.path + ': ' + errorToString(error));
        return {
            instantiated: false,
            note: 'Mesh asset import succeeded but instantiation failed: ' + errorToString(error)
        };
    }
}

function normalizeInstantiatedChildren(instances, parentObject) {
    if (!instances) {
        return;
    }

    for (let i = 0; i < instances.length; i++) {
        const item = instances[i];
        if (!item || Editor.isNull(item)) {
            continue;
        }

        if (typeof item.setParent === 'function') {
            item.setParent(parentObject);
            setLocalTransform(item, identityTransform());
        }
    }
}

async function instantiateAssetUnderParent(assetManager, asset, parentObject) {
    try {
        return await assetManager.instantiate([asset], { parents: [parentObject] });
    } catch (paramsError) {
        const instances = await assetManager.instantiate([asset]);
        normalizeInstantiatedChildren(instances, parentObject);
        return instances;
    }
}

function applyUslsMaterialsToInstances(instances, parentObject, source, materialAssets, importedAssets) {
    const materialRefs = getMaterialRefsForSource(source);
    if (materialRefs.length === 0) {
        return 0;
    }

    const visuals = collectVisualsForInstances(instances, parentObject);
    if (visuals.length === 0) {
        return 0;
    }

    let assigned = 0;
    for (let i = 0; i < visuals.length; i++) {
        const visual = visuals[i];
        const materialCount = assignMaterialsToVisual(visual, materialRefs, materialAssets);
        applyPassOverridesToVisual(visual, materialRefs[0], importedAssets);
        assigned += materialCount;
    }

    return assigned;
}

function assignImportedPrimitiveMaterialToInstances(assetManager, instances, parentObject, source, shape) {
    const normalizedShape = String(shape || '').toLowerCase();
    if (!normalizedShape) {
        return 0;
    }

    const variant = createPrimitiveVariant(source, normalizedShape);
    const material = findAssetByName(assetManager, variant.materialName, 'Material');
    if (!material || Editor.isNull(material)) {
        return 0;
    }

    const visuals = collectVisualsForInstances(instances, parentObject);
    return assignMaterialListToVisuals(visuals, [material]);
}

function assignMaterialsToVisual(visual, materialRefs, materialAssets) {
    const materials = [];
    for (let i = 0; i < materialRefs.length; i++) {
        const materialRef = materialRefs[i];
        const key = materialRef.assetId || createMaterialKey(materialRef);
        const material = materialAssets[key];
        if (material && !Editor.isNull(material)) {
            materials.push(material);
        }
    }

    if (materials.length === 0) {
        return 0;
    }

    return assignMaterialListToVisual(visual, materials);
}

function assignMaterialListToVisuals(visuals, materials) {
    let assigned = 0;
    for (let i = 0; i < visuals.length; i++) {
        assigned += assignMaterialListToVisual(visuals[i], materials);
    }

    return assigned;
}

function assignMaterialListToVisual(visual, materials) {
    if (!visual || Editor.isNull(visual) || !materials || materials.length === 0) {
        return 0;
    }

    try {
        visual.materials = materials;
        return materials.length;
    } catch (error) {
        // Try targeted assignment fallbacks below.
    }

    let assigned = 0;
    const existingCount = getVisualMaterialCount(visual);
    for (let i = 0; i < materials.length; i++) {
        if (assignMaterialAt(visual, materials[i], i, existingCount)) {
            assigned++;
        }
    }

    return assigned;
}

function assignMaterialAt(visual, material, index, existingCount) {
    try {
        if (index === 0) {
            visual.mainMaterial = material;
            return true;
        }
    } catch (error) {
        // Read-only on some builds.
    }

    try {
        if (typeof visual.setMaterialAt === 'function' && existingCount > index) {
            visual.setMaterialAt(index, material);
            return true;
        }
    } catch (error) {
        // Try adding below.
    }

    try {
        if (typeof visual.addMaterialAt === 'function') {
            visual.addMaterialAt(material, index);
            return true;
        }
    } catch (error) {
        return false;
    }
}

function getVisualMaterialCount(visual) {
    try {
        if (typeof visual.getMaterialsCount === 'function') {
            return visual.getMaterialsCount();
        }
    } catch (error) {
        // Unknown count; fall back to add/mainMaterial paths.
    }

    return 0;
}

function applyPassOverridesToVisual(visual, materialRef, importedAssets) {
    if (!isMaterialRefValid(materialRef)) {
        return;
    }

    const color = colorToVec4(materialRef.baseColor, [1, 1, 1, 1]);
    const texture = materialRef.albedoTextureAssetId ? importedAssets[materialRef.albedoTextureAssetId] : null;

    try {
        if (visual.mainPass) {
            configurePass(visual.mainPass, materialRef, color, texture, null, null);
        }
    } catch (error) {
        // Some generated visuals do not expose mainPass in editor scripting.
    }

    try {
        if (visual.mainPassOverrides) {
            setPassValue(visual.mainPassOverrides, 'baseColor', color);
            if (texture && !Editor.isNull(texture)) {
                setPassValue(visual.mainPassOverrides, 'baseTex', texture);
            }
        }
    } catch (error) {
        // Overrides are optional.
    }

    try {
        if (visual.propertyOverrides) {
            setPassValue(visual.propertyOverrides, 'baseColor', color);
            setPassValue(visual.propertyOverrides, 'color', color);
            if (texture && !Editor.isNull(texture)) {
                setPassValue(visual.propertyOverrides, 'baseTex', texture);
            }
        }
    } catch (error) {
        // Property overrides are optional.
    }

    try {
        if (visual.mainMaterial) {
            configureRuntimeMaterial(visual.mainMaterial, materialRef, importedAssets);
        }
    } catch (error) {
        // Main material access differs across imported asset types.
    }

    try {
        if (typeof visual.getMaterialsCount === 'function' && typeof visual.getMaterialAt === 'function') {
            const count = visual.getMaterialsCount();
            for (let i = 0; i < count; i++) {
                configureRuntimeMaterial(visual.getMaterialAt(i), materialRef, importedAssets);
            }
        }
    } catch (error) {
        // Material getters differ across editor/runtime surfaces.
    }
}

function collectVisualsForInstances(instances, parentObject) {
    const roots = [];
    if (parentObject) {
        roots.push(parentObject);
    }

    if (instances) {
        for (let i = 0; i < instances.length; i++) {
            roots.push(instances[i]);
        }
    }

    const visuals = [];
    const visitedObjects = {};
    const visitedVisuals = {};
    for (let i = 0; i < roots.length; i++) {
        collectVisualsRecursive(roots[i], visuals, visitedObjects, visitedVisuals);
    }

    return visuals;
}

function collectVisualsRecursive(sceneObject, visuals, visitedObjects, visitedVisuals) {
    if (!sceneObject || Editor.isNull(sceneObject) || typeof sceneObject.setParent !== 'function') {
        return;
    }

    const objectKey = entityKey(sceneObject);
    if (visitedObjects[objectKey]) {
        return;
    }
    visitedObjects[objectKey] = true;

    const components = getSceneObjectComponents(sceneObject);
    for (let i = 0; i < components.length; i++) {
        const component = components[i];
        if (isMaterialVisual(component)) {
            const visualKey = entityKey(component);
            if (!visitedVisuals[visualKey]) {
                visitedVisuals[visualKey] = true;
                visuals.push(component);
            }
        }
    }

    const children = getSceneObjectChildren(sceneObject);
    for (let i = 0; i < children.length; i++) {
        collectVisualsRecursive(children[i], visuals, visitedObjects, visitedVisuals);
    }
}

function getSceneObjectComponents(sceneObject) {
    const components = [];

    try {
        const directComponents = sceneObject.components || [];
        for (let i = 0; i < directComponents.length; i++) {
            components.push(directComponents[i]);
        }
    } catch (error) {
        // Continue with typed lookups.
    }

    const componentTypes = ['RenderMeshVisual', 'MaterialMeshVisual', 'Component.RenderMeshVisual'];
    for (let i = 0; i < componentTypes.length; i++) {
        try {
            if (typeof sceneObject.getComponents === 'function') {
                const found = sceneObject.getComponents(componentTypes[i]) || [];
                for (let j = 0; j < found.length; j++) {
                    components.push(found[j]);
                }
            }
        } catch (error) {
            // Component type strings vary between Lens versions.
        }
    }

    return components;
}

function getSceneObjectChildren(sceneObject) {
    try {
        if (sceneObject.children) {
            return sceneObject.children;
        }
    } catch (error) {
        // Continue with method fallback.
    }

    const children = [];
    try {
        if (typeof sceneObject.getChildrenCount === 'function' && typeof sceneObject.getChildAt === 'function') {
            const count = sceneObject.getChildrenCount();
            for (let i = 0; i < count; i++) {
                children.push(sceneObject.getChildAt(i));
            }
        }
    } catch (error) {
        // No children available.
    }

    return children;
}

function isMaterialVisual(component) {
    if (!component || Editor.isNull(component)) {
        return false;
    }

    if (typeof component.addMaterialAt === 'function' ||
        typeof component.setMaterialAt === 'function' ||
        component.mainMaterial !== undefined ||
        component.mainPass !== undefined) {
        return true;
    }

    try {
        const typeName = component.getTypeName ? component.getTypeName() : component.type;
        return String(typeName).indexOf('RenderMeshVisual') >= 0 ||
            String(typeName).indexOf('MaterialMeshVisual') >= 0;
    } catch (error) {
        return false;
    }
}

function entityKey(entity) {
    try {
        if (entity.id) {
            return entity.id.toString();
        }
    } catch (error) {
        // Fall back to name/type.
    }

    return safe(entity.name, '') + ':' + safe(entity.type, '') + ':' + Math.random().toString();
}

function collectPrimitiveVariants(manifest) {
    const variants = {};
    const objects = manifest.objects || [];
    for (let i = 0; i < objects.length; i++) {
        const object = objects[i];
        if (hasPrimitivePayload(object)) {
            const shape = String(object.primitive.shape).toLowerCase();
            if (shape === 'box' || shape === 'plane' || shape === 'sphere' || shape === 'cylinder' || shape === 'capsule') {
                const variant = createPrimitiveVariant(object, shape);
                variants[variant.key] = variant;
            }
        }
    }

    return variants;
}

function createPrimitiveVariant(source, shape) {
    const materialRef = getMaterialRefsForSource(source)[0] || null;
    const materialKey = materialRef ? materialRef.assetId || createMaterialKey(materialRef) : 'default';
    const color = materialRef && isColorArray(materialRef.baseColor) ? materialRef.baseColor : [1, 1, 1, 1];
    const key = sanitizeAssetName(shape + '_' + GENERATED_PRIMITIVE_ASSET_VERSION + '_' + stableShortKey(materialKey + '_' + colorKey(color)));
    const materialName = 'usls_mat_' + key;

    return {
        key: key,
        shape: shape,
        materialRef: materialRef,
        materialName: materialName,
        objFileName: 'usls_' + key + '.obj',
        mtlFileName: 'usls_' + key + '.mtl',
        color: color
    };
}

function primitiveAssetKey(source) {
    if (!hasPrimitivePayload(source)) {
        return '';
    }

    return createPrimitiveVariant(source, String(source.primitive.shape).toLowerCase()).key;
}

function stableShortKey(value) {
    let hash = 2166136261;
    const text = String(value || '');
    for (let i = 0; i < text.length; i++) {
        hash ^= text.charCodeAt(i);
        hash = Math.imul(hash, 16777619);
    }

    return (hash >>> 0).toString(16);
}

function createPrimitiveMtl(variant) {
    const color = variant.color || [1, 1, 1, 1];
    const r = fmt(numberOr(color[0], 1));
    const g = fmt(numberOr(color[1], 1));
    const b = fmt(numberOr(color[2], 1));
    const a = fmt(numberOr(color[3], 1));
    const lines = [];

    lines.push('# Generated by Unity2Snap');
    lines.push('newmtl ' + variant.materialName);
    lines.push('Ka ' + r + ' ' + g + ' ' + b);
    lines.push('Kd ' + r + ' ' + g + ' ' + b);
    lines.push('Ke ' + r + ' ' + g + ' ' + b);
    lines.push('Ks 0.000000 0.000000 0.000000');
    lines.push('Ns 1.000000');
    lines.push('d ' + a);
    lines.push('illum 0');
    lines.push('');

    return lines.join('\n');
}

function createPrimitiveObj(shape, materialName, mtlFileName) {
    if (shape === 'box') {
        return createBoxObj(materialName, mtlFileName);
    }

    if (shape === 'plane') {
        return createPlaneObj(materialName, mtlFileName);
    }

    if (shape === 'sphere') {
        return createSphereObj(16, 16, materialName, mtlFileName);
    }

    if (shape === 'cylinder') {
        return createCylinderObj(32, materialName, mtlFileName);
    }

    if (shape === 'capsule') {
        return createCapsuleObj(24, 6, materialName, mtlFileName);
    }

    return null;
}

function createBoxObj(materialName, mtlFileName) {
    const s = 50;
    const vertices = [
        [-s, -s, -s], [s, -s, -s], [s, s, -s], [-s, s, -s],
        [-s, -s, s], [s, -s, s], [s, s, s], [-s, s, s]
    ];
    const faces = [
        [1, 2, 3, 4],
        [5, 8, 7, 6],
        [1, 5, 6, 2],
        [2, 6, 7, 3],
        [3, 7, 8, 4],
        [4, 8, 5, 1]
    ];

    return objFromVerticesAndFaces('usls_box_100cm', vertices, faces, materialName, mtlFileName);
}

function createPlaneObj(materialName, mtlFileName) {
    const s = 500;
    const vertices = [
        [-s, 0, -s],
        [s, 0, -s],
        [s, 0, s],
        [-s, 0, s]
    ];
    const faces = [
        [1, 4, 3, 2]
    ];

    return objFromVerticesAndFaces('usls_plane_1000cm', vertices, faces, materialName, mtlFileName);
}

function createCylinderObj(segments, materialName, mtlFileName) {
    const radius = 50;
    const halfHeight = 100;
    const vertices = [];
    const faces = [];

    for (let i = 0; i < segments; i++) {
        const angle = 2 * Math.PI * i / segments;
        const x = Math.cos(angle) * radius;
        const z = Math.sin(angle) * radius;
        vertices.push([x, -halfHeight, z]);
        vertices.push([x, halfHeight, z]);
    }

    const bottomCenter = vertices.length + 1;
    vertices.push([0, -halfHeight, 0]);
    const topCenter = vertices.length + 1;
    vertices.push([0, halfHeight, 0]);

    for (let i = 0; i < segments; i++) {
        const next = (i + 1) % segments;
        const b0 = i * 2 + 1;
        const t0 = i * 2 + 2;
        const b1 = next * 2 + 1;
        const t1 = next * 2 + 2;
        faces.push([b0, b1, t1, t0]);
        faces.push([bottomCenter, b0, b1]);
        faces.push([topCenter, t1, t0]);
    }

    return objFromVerticesAndFaces('usls_cylinder_100x200cm', vertices, faces, materialName, mtlFileName);
}

function createCapsuleObj(segments, hemisphereRings, materialName, mtlFileName) {
    const radius = 50;
    const halfCylinderHeight = 50;
    const rings = [];

    rings.push({ y: -halfCylinderHeight - radius, radius: 0 });

    for (let i = 1; i <= hemisphereRings; i++) {
        const angle = -Math.PI / 2 + (Math.PI / 2) * (i / hemisphereRings);
        rings.push({
            y: -halfCylinderHeight + Math.sin(angle) * radius,
            radius: Math.cos(angle) * radius
        });
    }

    rings.push({ y: halfCylinderHeight, radius: radius });

    for (let i = 1; i <= hemisphereRings; i++) {
        const angle = (Math.PI / 2) * (i / hemisphereRings);
        rings.push({
            y: halfCylinderHeight + Math.sin(angle) * radius,
            radius: Math.cos(angle) * radius
        });
    }

    const vertices = [];
    const faces = [];
    for (let ringIndex = 0; ringIndex < rings.length; ringIndex++) {
        const ring = rings[ringIndex];
        for (let segment = 0; segment < segments; segment++) {
            const angle = 2 * Math.PI * segment / segments;
            vertices.push([
                Math.cos(angle) * ring.radius,
                ring.y,
                Math.sin(angle) * ring.radius
            ]);
        }
    }

    for (let ringIndex = 0; ringIndex < rings.length - 1; ringIndex++) {
        for (let segment = 0; segment < segments; segment++) {
            const next = (segment + 1) % segments;
            const a = ringIndex * segments + segment + 1;
            const b = ringIndex * segments + next + 1;
            const c = (ringIndex + 1) * segments + next + 1;
            const d = (ringIndex + 1) * segments + segment + 1;
            faces.push([a, b, c, d]);
        }
    }

    return objFromVerticesAndFaces('usls_capsule_100x200cm', vertices, faces, materialName, mtlFileName);
}

function createSphereObj(segments, rings, materialName, mtlFileName) {
    const radius = 50;
    const vertices = [];
    const faces = [];

    for (let ring = 0; ring <= rings; ring++) {
        const v = ring / rings;
        const theta = Math.PI * v;
        const y = Math.cos(theta) * radius;
        const ringRadius = Math.sin(theta) * radius;

        for (let segment = 0; segment < segments; segment++) {
            const u = segment / segments;
            const phi = 2 * Math.PI * u;
            const x = Math.cos(phi) * ringRadius;
            const z = Math.sin(phi) * ringRadius;
            vertices.push([x, y, z]);
        }
    }

    for (let ring = 0; ring < rings; ring++) {
        for (let segment = 0; segment < segments; segment++) {
            const next = (segment + 1) % segments;
            const a = ring * segments + segment + 1;
            const b = ring * segments + next + 1;
            const c = (ring + 1) * segments + next + 1;
            const d = (ring + 1) * segments + segment + 1;

            if (ring === 0) {
                faces.push([a, c, d]);
            } else if (ring === rings - 1) {
                faces.push([a, b, d]);
            } else {
                faces.push([a, b, c, d]);
            }
        }
    }

    return objFromVerticesAndFaces('usls_sphere_100cm', vertices, faces, materialName, mtlFileName);
}

function objFromVerticesAndFaces(name, vertices, faces, materialName, mtlFileName) {
    const lines = [];
    lines.push('# Generated by Unity2Snap');
    if (mtlFileName) {
        lines.push('mtllib ' + mtlFileName);
    }
    lines.push('o ' + name);

    for (let i = 0; i < vertices.length; i++) {
        const v = vertices[i];
        lines.push('v ' + fmt(v[0]) + ' ' + fmt(v[1]) + ' ' + fmt(v[2]));
    }

    if (materialName) {
        lines.push('usemtl ' + materialName);
    }

    for (let i = 0; i < faces.length; i++) {
        lines.push('f ' + faces[i].join(' '));
    }

    lines.push('');
    return lines.join('\n');
}

function fmt(value) {
    return Number(value).toFixed(6);
}

function createLightComponent(sceneObject, light) {
    try {
        const component = sceneObject.addComponent('LightSource');
        component.LightType = mapLightType(light.lightType);
        component.Intensity = numberOr(light.intensity, 1);
        component.DecayRange = numberOr(light.range, 10);
        component.OuterConeAngle = numberOr(light.spotAngle, 45);

        if (light.color && light.color.length >= 3) {
            component.Color = new vec4(
                numberOr(light.color[0], 1),
                numberOr(light.color[1], 1),
                numberOr(light.color[2], 1),
                1
            );
        }

        return true;
    } catch (error) {
        console.warn('[Unity2Snap] LightSource creation failed: ' + errorToString(error));
        addImportNote(sceneObject, 'Light metadata only. Component creation failed.');
        return false;
    }
}

function decorateCameraHint(sceneObject, camera) {
    const pieces = [];
    pieces.push('Camera hint only. Spectacles/Lens camera remains device driven.');
    if (camera) {
        pieces.push('fov=' + safeNumber(camera.fieldOfView));
        pieces.push('near=' + safeNumber(camera.nearClip));
        pieces.push('far=' + safeNumber(camera.farClip));
    }

    try {
        const component = sceneObject.addComponent('Camera');
        component.enabled = false;
    } catch (error) {
        // Spectacles projects usually keep the device camera; a marker note is enough.
    }

    addImportNote(sceneObject, pieces.join(' '));
}

function createColliderComponent(sceneObject, collider) {
    try {
        const component = addPhysicsComponent(sceneObject, collider);
        component.debugDrawEnabled = true;
        component.intangible = collider.trigger === true;

        if (collider.physicsMode === 'dynamic' && 'dynamic' in component) {
            component.dynamic = true;
        }

        const shape = createPhysicsShape(collider);
        if (shape) {
            component.shape = shape;
            component.fitVisual = false;
            addImportNote(sceneObject, 'Physics collider created. type=' + safe(collider.colliderType, 'unknown'));
        } else {
            component.fitVisual = true;
            addImportNote(sceneObject, 'Physics collider created with fitVisual fallback. sourceType=' + safe(collider.colliderType, 'unknown'));
        }

        return { created: true };
    } catch (error) {
        console.warn('[Unity2Snap] Collider creation failed: ' + errorToString(error));
        return {
            created: false,
            note: 'Collider metadata only. Component creation failed: ' + errorToString(error)
        };
    }
}

function addPhysicsComponent(sceneObject, collider) {
    if (collider.physicsMode === 'dynamic') {
        try {
            return sceneObject.addComponent('Physics.BodyComponent');
        } catch (error) {
            try {
                return sceneObject.addComponent('BodyComponent');
            } catch (fallbackError) {
                // Fall through to a static collider if this Lens Studio build does not expose BodyComponent here.
            }
        }
    }

    try {
        return sceneObject.addComponent('Physics.ColliderComponent');
    } catch (error) {
        return sceneObject.addComponent('ColliderComponent');
    }
}

function createPhysicsShape(collider) {
    if (typeof Shape === 'undefined') {
        return null;
    }

    try {
        if (collider.colliderType === 'box' && typeof Shape.createBoxShape === 'function') {
            const shape = Shape.createBoxShape();
            shape.size = toVec3(collider.size, [100, 100, 100]);
            return shape;
        }

        if (collider.colliderType === 'sphere' && typeof Shape.createSphereShape === 'function') {
            const shape = Shape.createSphereShape();
            shape.radius = numberOr(collider.radius, 50);
            return shape;
        }

        if (collider.colliderType === 'capsule' && typeof Shape.createCapsuleShape === 'function') {
            const shape = Shape.createCapsuleShape();
            shape.radius = numberOr(collider.radius, 50);
            shape.length = numberOr(collider.height, 200);
            const axis = capsuleAxis(collider.direction);
            if (axis !== undefined) {
                shape.axis = axis;
            }
            return shape;
        }
    } catch (error) {
        console.warn('[Unity2Snap] Physics shape creation failed: ' + errorToString(error));
    }

    return null;
}

function capsuleAxis(direction) {
    if (typeof Axis === 'undefined') {
        return undefined;
    }

    if (direction === 0) {
        return Axis.X;
    }

    if (direction === 2) {
        return Axis.Z;
    }

    return Axis.Y;
}

function attachWarningSummary(scene, root, manifest, result) {
    const warnings = manifest.warnings || [];
    if (warnings.length === 0) {
        return;
    }

    const note = scene.createSceneObject('USLS Import Warnings');
    note.setParent(root);
    setLocalTransform(note, identityTransform());

    const grouped = {};
    for (let i = 0; i < warnings.length; i++) {
        const warning = warnings[i];
        const key = safe(warning.severity, 'warning') + ' / ' + safe(warning.code, 'UNKNOWN');
        grouped[key] = (grouped[key] || 0) + 1;
    }

    const pieces = [];
    const keys = Object.keys(grouped);
    for (let i = 0; i < keys.length; i++) {
        pieces.push(keys[i] + ': ' + grouped[keys[i]]);
    }

    addImportNote(note, pieces.join(' | '));
    result.notes.push('Warning summary object created.');
}

function addImportNote(sceneObject, text) {
    if (sceneObject.name.indexOf(' [USLS]') < 0) {
        sceneObject.name = sceneObject.name + ' [USLS]';
    }
    console.log('[Unity2Snap] ' + sceneObject.name + ': ' + text);
}

function setLocalTransform(sceneObject, transform) {
    const current = sceneObject.localTransform;
    const data = transform || identityTransform();
    current.position = toVec3(data.position, [0, 0, 0]);
    current.rotation = toVec3(data.rotation, [0, 0, 0]);
    current.scale = toVec3(data.scale, [1, 1, 1]);
    sceneObject.localTransform = current;
}

function applyPlayerSpawnOffset(root, playerSpawn, objectById, sourceById) {
    if (!playerSpawn) {
        return;
    }

    const worldPosition = getSceneObjectWorldPosition(objectById[playerSpawn.id]) ||
        computeManifestWorldPosition(playerSpawn, sourceById);

    if (!worldPosition) {
        return;
    }

    const transform = root.localTransform;
    transform.position = new vec3(
        -numberOr(worldPosition[0], 0),
        -numberOr(worldPosition[1], 0),
        -numberOr(worldPosition[2], 0)
    );
    root.localTransform = transform;
}

function findPlayerSpawn(objects) {
    let first = null;
    for (let i = 0; i < objects.length; i++) {
        const obj = objects[i];
        if (obj.type === 'player_spawn') {
            if (!first) {
                first = obj;
            }

            if (hasTag(obj, 'vr_rig_root') || hasTag(obj, 'spectacles_origin_reference')) {
                return obj;
            }
        }
    }

    return first;
}

function hasTag(object, tag) {
    const tags = object && object.tags ? object.tags : [];
    for (let i = 0; i < tags.length; i++) {
        if (tags[i] === tag) {
            return true;
        }
    }

    return false;
}

function buildSourceById(objects) {
    const byId = {};
    for (let i = 0; i < objects.length; i++) {
        const object = objects[i];
        if (object && object.id) {
            byId[object.id] = object;
        }
    }

    return byId;
}

function getSceneObjectWorldPosition(sceneObject) {
    if (!sceneObject) {
        return null;
    }

    try {
        if (sceneObject.worldTransform && sceneObject.worldTransform.position) {
            const p = sceneObject.worldTransform.position;
            return [p.x, p.y, p.z];
        }
    } catch (error) {
        // Fall back to manifest math below.
    }

    return null;
}

function computeManifestWorldPosition(source, sourceById) {
    const chain = [];
    let current = source;
    let guard = 0;
    while (current && guard < 256) {
        chain.unshift(current);
        current = current.parentId ? sourceById[current.parentId] : null;
        guard++;
    }

    let position = [0, 0, 0];
    for (let i = 0; i < chain.length; i++) {
        const local = chain[i].transform && chain[i].transform.position ? chain[i].transform.position : [0, 0, 0];
        position = [
            position[0] + numberOr(local[0], 0),
            position[1] + numberOr(local[1], 0),
            position[2] + numberOr(local[2], 0)
        ];
    }

    return position;
}

function mapLightType(value) {
    const lower = safe(value, '').toLowerCase();
    if (lower === 'directional') {
        return Editor.Components.LightType.Directional;
    }
    if (lower === 'spot') {
        return Editor.Components.LightType.Spot;
    }
    if (lower === 'point') {
        return Editor.Components.LightType.Point;
    }
    return Editor.Components.LightType.Point;
}

function pathFromRelative(exportDir, relativePath) {
    const normalized = String(relativePath).replace(/\\/g, '/');
    const pieces = normalized.split('/');
    let path = new Editor.Path(exportDir);
    for (let i = 0; i < pieces.length; i++) {
        if (pieces[i]) {
            path = path.appended(pieces[i]);
        }
    }
    return path;
}

function ensureDirectory(path) {
    if (fs.exists(path) && fs.isDirectory(path)) {
        return;
    }

    try {
        const options = new fs.CreateDirOptions();
        options.recursive = true;
        fs.createDir(path, options);
    } catch (error) {
        try {
            fs.createDir(path, { recursive: true });
        } catch (fallbackError) {
            throw new Error('Failed to create directory ' + path.toString() + ': ' + errorToString(fallbackError));
        }
    }
}

function assetFromMeta(fileMeta, assetManager) {
    if (!fileMeta) {
        return null;
    }

    const assets = assetManager.assets || [];
    for (let i = 0; i < assets.length; i++) {
        const asset = assets[i];
        try {
            if (asset && asset.fileMeta && asset.fileMeta.isSame && asset.fileMeta.isSame(fileMeta)) {
                return asset;
            }
        } catch (e) {
            // Keep scanning. Asset metadata shape can vary by asset type.
        }
    }

    return null;
}

function validateManifest(manifest) {
    if (!manifest || !manifest.objects || !Array.isArray(manifest.objects)) {
        throw new Error('Invalid USLS manifest: missing objects array.');
    }

    if (!manifest.version) {
        throw new Error('Invalid USLS manifest: missing version.');
    }
}

function buildAssetPolicy(manifest) {
    const sourceCounts = {};
    const repeatedSourceAssetIds = {};
    const assets = manifest.assets || [];

    for (let i = 0; i < assets.length; i++) {
        const asset = assets[i];
        if (asset && asset.type === 'mesh' && asset.sourcePath && asset.importHint === 'copied_source_model') {
            sourceCounts[asset.sourcePath] = (sourceCounts[asset.sourcePath] || 0) + 1;
        }
    }

    for (let i = 0; i < assets.length; i++) {
        const asset = assets[i];
        if (asset && asset.id && asset.sourcePath && sourceCounts[asset.sourcePath] > 1) {
            repeatedSourceAssetIds[asset.id] = true;
        }
    }

    return { repeatedSourceAssetIds: repeatedSourceAssetIds };
}

function countRepeatedSourceModelRisks(manifest) {
    const policy = buildAssetPolicy(manifest);
    return Object.keys(policy.repeatedSourceAssetIds).length;
}

function countPrimitives(manifest) {
    const objects = manifest.objects || [];
    let count = 0;
    for (let i = 0; i < objects.length; i++) {
        if (objects[i] && objects[i].type === 'primitive') {
            count++;
        }
    }

    return count;
}

function countPlayerSpawns(manifest) {
    const objects = manifest.objects || [];
    let count = 0;
    for (let i = 0; i < objects.length; i++) {
        if (objects[i] && objects[i].type === 'player_spawn') {
            count++;
        }
    }

    return count;
}

function countImportableAssets(manifest) {
    const assets = manifest.assets || [];
    let count = 0;
    for (let i = 0; i < assets.length; i++) {
        if (assets[i] && assets[i].path) {
            count++;
        }
    }
    return count;
}

function countMaterialDefinitions(manifest) {
    return collectMaterialRefs(manifest).length;
}

function countWarnings(manifest) {
    const warnings = manifest.warnings || [];
    let count = 0;
    for (let i = 0; i < warnings.length; i++) {
        if (!warnings[i] || warnings[i].severity !== 'info') {
            count++;
        }
    }
    return count;
}

function identityTransform() {
    return {
        position: [0, 0, 0],
        rotation: [0, 0, 0],
        scale: [1, 1, 1]
    };
}

function toVec3(value, fallback) {
    const source = Array.isArray(value) ? value : fallback;
    return new vec3(
        numberOr(source[0], fallback[0]),
        numberOr(source[1], fallback[1]),
        numberOr(source[2], fallback[2])
    );
}

function colorToVec4(value, fallback) {
    const source = isColorArray(value) ? value : fallback;
    return new vec4(
        numberOr(source[0], fallback[0]),
        numberOr(source[1], fallback[1]),
        numberOr(source[2], fallback[2]),
        numberOr(source[3], fallback[3])
    );
}

function isColorArray(value) {
    return Array.isArray(value) && value.length >= 3;
}

function isMeaningfulColor(value) {
    if (!isColorArray(value)) {
        return false;
    }

    const alpha = value.length >= 4 ? numberOr(value[3], 1) : 1;
    return alpha > 0.0001 ||
        Math.abs(numberOr(value[0], 0)) > 0.0001 ||
        Math.abs(numberOr(value[1], 0)) > 0.0001 ||
        Math.abs(numberOr(value[2], 0)) > 0.0001;
}

function hasLightPayload(light) {
    return !!light && !!safe(light.lightType, '');
}

function hasPrimitivePayload(source) {
    return !!source && !!source.primitive && !!safe(source.primitive.shape, '');
}

function hasMeshPayload(mesh) {
    return !!mesh && (!!safe(mesh.assetId, '') || !!safe(mesh.assetRef, '') || numberOr(mesh.vertexCount, 0) > 0);
}

function hasCameraPayload(source) {
    if (!source) {
        return false;
    }

    return source.type === 'camera_hint' ||
        !!source.camera && (
            numberOr(source.camera.fieldOfView, 0) > 0 ||
            numberOr(source.camera.orthographicSize, 0) > 0 ||
            numberOr(source.camera.farClip, 0) > 0
        );
}

function hasColliderPayload(collider) {
    return !!collider && !!safe(collider.colliderType, '');
}

function numberOr(value, fallback) {
    const number = Number(value);
    return isFinite(number) ? number : fallback;
}

function safe(value, fallback) {
    if (value === null || value === undefined || value === '') {
        return fallback;
    }

    return String(value);
}

function safeNumber(value) {
    return numberOr(value, 0).toString();
}

function normalizePath(value) {
    return String(value || '').trim().replace(/\\/g, '/');
}
