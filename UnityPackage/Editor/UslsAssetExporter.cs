using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Unity2Snap.Editor
{
    internal sealed class UslsAssetExporter
    {
        private static readonly HashSet<string> SupportedMeshExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".fbx",
            ".obj",
            ".gltf",
            ".glb"
        };

        private static readonly HashSet<string> SupportedTextureExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".png",
            ".jpg",
            ".jpeg",
            ".gif"
        };

        private readonly UslsExportSettings settings;
        private readonly UslsManifest manifest;
        private readonly UslsWarningSink warnings;
        private readonly string outputDirectory;
        private readonly Dictionary<int, UslsAsset> meshAssets = new Dictionary<int, UslsAsset>();
        private readonly Dictionary<int, UslsAsset> textureAssets = new Dictionary<int, UslsAsset>();
        private readonly Dictionary<int, string> materialAssetIds = new Dictionary<int, string>();

        public UslsAssetExporter(UslsExportSettings settings, UslsManifest manifest, UslsWarningSink warnings)
        {
            this.settings = settings;
            this.manifest = manifest;
            this.warnings = warnings;
            outputDirectory = settings.OutputDirectory;
        }

        public UslsAsset GetOrCreateMeshAsset(Mesh mesh, string objectId, string objectPath)
        {
            if (mesh == null)
            {
                return null;
            }

            var key = mesh.GetInstanceID();
            UslsAsset existing;
            if (meshAssets.TryGetValue(key, out existing))
            {
                return existing;
            }

            var sourcePath = AssetDatabase.GetAssetPath(mesh);
            var assetId = UslsFileUtility.StableId("mesh", string.IsNullOrEmpty(sourcePath) ? mesh.name + key : sourcePath + ":" + mesh.name);
            var asset = new UslsAsset
            {
                id = assetId,
                type = UslsSchema.AssetMesh,
                name = mesh.name,
                sourcePath = sourcePath,
                importHint = "metadata_only",
                vertexCount = mesh.vertexCount,
                triangleCount = CountTriangles(mesh)
            };

            if (settings.CopySupportedSourceAssets && TryCopySourceAsset(sourcePath, "assets/meshes", assetId, out var relativePath))
            {
                asset.path = relativePath;
                asset.importHint = settings.AnalyzeOnly ? "would_copy_source_model" : "copied_source_model";
            }
            else if (!IsBuiltInAssetPath(sourcePath))
            {
                warnings.Add(
                    "warning",
                    "MESH_FILE_EXPORT_REQUIRED",
                    objectId,
                    objectPath,
                    "Mesh metadata was exported, but no Lens-importable mesh file was written.",
                    "Install a glTF/GLB or FBX exporter integration, or use a mesh whose source asset is FBX, OBJ, glTF, or GLB.");
            }

            meshAssets.Add(key, asset);
            manifest.assets.Add(asset);
            return asset;
        }

        public UslsAsset GetOrCreateTextureAsset(Texture texture, string objectId, string objectPath)
        {
            if (texture == null)
            {
                return null;
            }

            var key = texture.GetInstanceID();
            UslsAsset existing;
            if (textureAssets.TryGetValue(key, out existing))
            {
                return existing;
            }

            var sourcePath = AssetDatabase.GetAssetPath(texture);
            var assetId = UslsFileUtility.StableId("tex", string.IsNullOrEmpty(sourcePath) ? texture.name + key : sourcePath);
            var asset = new UslsAsset
            {
                id = assetId,
                type = UslsSchema.AssetTexture,
                name = texture.name,
                sourcePath = sourcePath,
                importHint = "metadata_only",
                width = texture.width,
                height = texture.height
            };

            if (settings.CopySupportedSourceAssets && TryCopySourceAsset(sourcePath, "assets/textures", assetId, out var relativePath))
            {
                asset.path = relativePath;
                asset.importHint = settings.AnalyzeOnly ? "would_copy_source_texture" : "copied_source_texture";
            }
            else if (settings.CopySupportedSourceAssets && TryWriteTexturePng(texture, "assets/textures", assetId, out relativePath))
            {
                asset.path = relativePath;
                asset.importHint = settings.AnalyzeOnly ? "would_bake_png_texture" : "baked_png_texture";
                warnings.Add(
                    "info",
                    settings.AnalyzeOnly ? "TEXTURE_CAN_BAKE_TO_PNG" : "TEXTURE_BAKED_TO_PNG",
                    objectId,
                    objectPath,
                    settings.AnalyzeOnly ? "Texture can be baked to PNG during export." : "Texture was baked to PNG for Lens Studio import.",
                    "Validate color space and compression in Lens Studio for final Spectacles builds.");
            }
            else if (!string.IsNullOrEmpty(sourcePath))
            {
                warnings.Add(
                    "warning",
                    "TEXTURE_FILE_NOT_COPIED",
                    objectId,
                    objectPath,
                    "Texture metadata was exported, but the source texture was not copied.",
                    "Use PNG or JPG textures for first-pass Lens Studio compatibility.");
            }

            if (texture.width > 2048 || texture.height > 2048)
            {
                warnings.Add(
                    "warning",
                    "TEXTURE_OVER_2048",
                    objectId,
                    objectPath,
                    "Texture is larger than 2048 pixels on at least one side.",
                    "Resize or author a Lens-specific texture variant before import.");
            }

            textureAssets.Add(key, asset);
            manifest.assets.Add(asset);
            return asset;
        }

        public string GetOrCreateMaterialAssetId(Material material)
        {
            if (material == null)
            {
                return null;
            }

            var key = material.GetInstanceID();
            string existing;
            if (materialAssetIds.TryGetValue(key, out existing))
            {
                return existing;
            }

            var sourcePath = AssetDatabase.GetAssetPath(material);
            var assetId = UslsFileUtility.StableId("mat", string.IsNullOrEmpty(sourcePath) ? material.name + key : sourcePath);
            materialAssetIds.Add(key, assetId);
            manifest.assets.Add(new UslsAsset
            {
                id = assetId,
                type = UslsSchema.AssetMaterial,
                name = material.name,
                sourcePath = sourcePath,
                importHint = "manifest_material_metadata"
            });
            return assetId;
        }

        public int MeshAssetCount => meshAssets.Count;
        public int TextureAssetCount => textureAssets.Count;
        public int MaterialAssetCount => materialAssetIds.Count;

        public static int CountTriangles(Mesh mesh)
        {
            if (mesh == null)
            {
                return 0;
            }

            var triangles = 0;
            for (var i = 0; i < mesh.subMeshCount; i++)
            {
                triangles += (int)(mesh.GetIndexCount(i) / 3);
            }

            return triangles;
        }

        private bool TryCopySourceAsset(string sourcePath, string relativeFolder, string assetId, out string relativePath)
        {
            relativePath = null;
            if (string.IsNullOrEmpty(sourcePath) || IsBuiltInAssetPath(sourcePath))
            {
                return false;
            }

            var extension = Path.GetExtension(sourcePath);
            var assetKindSupported = SupportedMeshExtensions.Contains(extension) || SupportedTextureExtensions.Contains(extension);
            if (!assetKindSupported)
            {
                return false;
            }

            var absoluteSource = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(Application.dataPath) ?? string.Empty, sourcePath));
            if (!File.Exists(absoluteSource))
            {
                return false;
            }

            var safeName = UslsFileUtility.SanitizeFileName(Path.GetFileNameWithoutExtension(sourcePath));
            var fileName = safeName + "_" + assetId.Substring(assetId.Length - 8) + extension.ToLowerInvariant();
            relativePath = UslsFileUtility.CombineRelative(relativeFolder, fileName);
            if (settings.AnalyzeOnly)
            {
                return true;
            }

            var outputFolder = Path.Combine(outputDirectory, relativeFolder.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(outputFolder);

            var destination = Path.Combine(outputFolder, fileName);
            File.Copy(absoluteSource, destination, true);

            return true;
        }

        private bool TryWriteTexturePng(Texture texture, string relativeFolder, string assetId, out string relativePath)
        {
            relativePath = null;
            if (texture == null || texture.width <= 0 || texture.height <= 0)
            {
                return false;
            }

            var maxDimension = Mathf.Max(texture.width, texture.height);
            var scale = maxDimension > 2048 ? 2048f / maxDimension : 1f;
            var width = Mathf.Max(1, Mathf.RoundToInt(texture.width * scale));
            var height = Mathf.Max(1, Mathf.RoundToInt(texture.height * scale));
            var safeSourceName = !string.IsNullOrEmpty(texture.name) ? texture.name : "texture";
            var safeName = UslsFileUtility.SanitizeFileName(safeSourceName);
            var fileName = safeName + "_" + assetId.Substring(assetId.Length - 8) + ".png";

            relativePath = UslsFileUtility.CombineRelative(relativeFolder, fileName);
            if (settings.AnalyzeOnly)
            {
                return true;
            }

            var previous = RenderTexture.active;
            var temporary = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            Texture2D readable = null;

            try
            {
                Graphics.Blit(texture, temporary);
                RenderTexture.active = temporary;
                readable = new Texture2D(width, height, TextureFormat.RGBA32, false);
                readable.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                readable.Apply();

                var bytes = readable.EncodeToPNG();
                if (bytes == null || bytes.Length == 0)
                {
                    return false;
                }

                var outputFolder = Path.Combine(outputDirectory, relativeFolder.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(outputFolder);

                var destination = Path.Combine(outputFolder, fileName);
                File.WriteAllBytes(destination, bytes);

                return true;
            }
            catch (Exception)
            {
                return false;
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(temporary);
                if (readable != null)
                {
                    UnityEngine.Object.DestroyImmediate(readable);
                }
            }
        }

        private static bool IsBuiltInAssetPath(string sourcePath)
        {
            return string.IsNullOrEmpty(sourcePath) ||
                   sourcePath.StartsWith("Library/", StringComparison.OrdinalIgnoreCase) ||
                   sourcePath.StartsWith("Resources/unity_builtin_extra", StringComparison.OrdinalIgnoreCase);
        }
    }
}
