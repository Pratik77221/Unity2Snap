using System;
using System.Collections.Generic;

namespace Unity2Snap
{
    [Serializable]
    public sealed class UslsManifest
    {
        public string version = UslsSchema.Version;
        public UslsExporterInfo exporter = new UslsExporterInfo();
        public UslsCoordinateInfo coordinateSystem = new UslsCoordinateInfo();
        public UslsExportOptions exportOptions = new UslsExportOptions();
        public UslsStats stats = new UslsStats();
        public List<UslsAsset> assets = new List<UslsAsset>();
        public List<UslsObject> objects = new List<UslsObject>();
        public List<UslsWarning> warnings = new List<UslsWarning>();
    }

    public static class UslsSchema
    {
        public const string Version = "0.1.0";

        public const string TypeEmpty = "empty";
        public const string TypeMesh = "mesh";
        public const string TypePrimitive = "primitive";
        public const string TypeLight = "light";
        public const string TypeCameraHint = "camera_hint";
        public const string TypeCollider = "collider";
        public const string TypePlayerSpawn = "player_spawn";

        public const string AssetMesh = "mesh";
        public const string AssetTexture = "texture";
        public const string AssetMaterial = "material";
    }

    [Serializable]
    public sealed class UslsExporterInfo
    {
        public string name = "Unity2Snap";
        public string packageVersion = UslsSchema.Version;
        public string unityVersion;
        public string exportedAtUtc;
        public string sceneName;
        public string scenePath;
    }

    [Serializable]
    public sealed class UslsCoordinateInfo
    {
        public string source = "unity";
        public string sourceHandedness = "left";
        public string sourceUpAxis = "Y";
        public string sourceForwardAxis = "Z";
        public string sourceUnit = "meter";
        public string target = "lens_studio";
        public string targetUnit = "centimeter";
        public float positionScale = 100f;
        public string rotationRepresentation = "euler_degrees";
        public string conversion = "unity_local_to_lens_local_v0";
    }

    [Serializable]
    public sealed class UslsExportOptions
    {
        public bool exportInactiveObjects = true;
        public bool includeEditorOnlyObjects;
        public bool includeMeshMetadata = true;
        public bool includeMaterialMetadata = true;
        public bool includeColliders = true;
        public bool includeLights = true;
        public bool includeCameraHints = true;
        public bool includePlayerSpawnMarkers = true;
        public bool includeUnityUiObjects;
        public bool convertMetersToCentimeters = true;
        public bool convertUnityToLensHandedness = true;
        public bool copySupportedSourceAssets = true;
    }

    [Serializable]
    public sealed class UslsStats
    {
        public int objectCount;
        public int meshObjectCount;
        public int primitiveObjectCount;
        public int lightCount;
        public int cameraHintCount;
        public int colliderCount;
        public int playerSpawnCount;
        public int warningCount;
        public int noticeCount;
        public int errorCount;
        public int skippedObjectCount;
        public int totalTriangles;
        public int materialCount;
        public int textureCount;
    }

    [Serializable]
    public sealed class UslsObject
    {
        public string id;
        public string parentId;
        public string sourceInstanceId;
        public string name;
        public string path;
        public string type;
        public bool enabled = true;
        public UslsTransform transform = new UslsTransform();
        public UslsMeshRef mesh;
        public UslsPrimitive primitive;
        public UslsMaterialRef material;
        public List<UslsMaterialRef> materials = new List<UslsMaterialRef>();
        public UslsLight light;
        public UslsCameraHint camera;
        public UslsCollider collider;
        public List<string> sourceComponents = new List<string>();
        public List<string> tags = new List<string>();
        public List<string> notes = new List<string>();
    }

    [Serializable]
    public sealed class UslsTransform
    {
        public float[] position = new float[3];
        public float[] rotation = new float[3];
        public float[] scale = new float[3];
    }

    [Serializable]
    public sealed class UslsMeshRef
    {
        public string assetId;
        public string assetRef;
        public string meshName;
        public int vertexCount;
        public int triangleCount;
        public int subMeshCount;
        public bool skinned;
    }

    [Serializable]
    public sealed class UslsPrimitive
    {
        public string shape;
        public string fallbackMeshAssetId;
    }

    [Serializable]
    public sealed class UslsMaterialRef
    {
        public string assetId;
        public string name;
        public string shaderName;
        public float[] baseColor = new float[4];
        public string albedoTextureAssetId;
        public string normalTextureAssetId;
        public string metallicTextureAssetId;
        public int slot;
        public float metallic;
        public float roughness = 0.5f;
        public bool transparent;
        public bool emission;
        public float[] emissionColor = new float[4];
    }

    [Serializable]
    public sealed class UslsLight
    {
        public string lightType;
        public float[] color = new float[3];
        public float intensity;
        public float range;
        public float spotAngle;
        public bool shadows;
    }

    [Serializable]
    public sealed class UslsCameraHint
    {
        public float fieldOfView;
        public bool orthographic;
        public float orthographicSize;
        public float nearClip;
        public float farClip;
        public string usage = "spectacles_device_camera_reference";
    }

    [Serializable]
    public sealed class UslsCollider
    {
        public string colliderType;
        public bool trigger;
        public string physicsMode;
        public float[] center = new float[3];
        public float[] size = new float[3];
        public float radius;
        public float height;
        public int direction;
        public string meshAssetId;
        public bool convex;
    }

    [Serializable]
    public sealed class UslsAsset
    {
        public string id;
        public string type;
        public string path;
        public string sourcePath;
        public string name;
        public string importHint;
        public int width;
        public int height;
        public int vertexCount;
        public int triangleCount;
    }

    [Serializable]
    public sealed class UslsWarning
    {
        public string id;
        public string severity;
        public string code;
        public string objectId;
        public string objectPath;
        public string message;
        public string recommendation;
    }
}
