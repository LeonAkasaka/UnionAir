using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace LeonAkasaka.UnionAir.Editor
{
    internal sealed class ModelImporterUpdateRequest
    {
        internal const int SchemaVersion = 1;

        internal ModelImporterModelPatch Model;
        internal ModelImporterMeshPatch Mesh;
        internal ModelImporterGeometryPatch Geometry;
        internal ModelImporterNormalsPatch Normals;
        internal ModelImporterTangentsPatch Tangents;
        internal ModelImporterMaterialsPatch Materials;
        internal List<ModelImporterMaterialRemapPatch> MaterialRemaps;
        internal ModelImporterRigPatch Rig;
        internal List<ModelImporterClipPatch> Clips;
        internal ModelImporterClipAnimation[] PreparedClips;

        internal bool HasAnySetting =>
            Model != null || Mesh != null || Geometry != null || Normals != null || Tangents != null ||
            Materials != null || MaterialRemaps != null || Rig != null || Clips != null;

        internal ModelImporterState Apply(ModelImporterState source, List<string> changedFields)
        {
            var result = source.Clone();
            Model?.Apply(result, changedFields);
            Mesh?.Apply(result, changedFields);
            Geometry?.Apply(result, changedFields);
            Normals?.Apply(result, changedFields);
            Tangents?.Apply(result, changedFields);
            Materials?.Apply(result, changedFields);
            Rig?.Apply(result, changedFields);
            if (MaterialRemaps != null)
                ModelImporterMaterialsRigState.ApplyRemapPatches(result, MaterialRemaps, changedFields);
            if (Clips != null)
                ModelImporterClipsState.ApplyPrepared(result, PreparedClips, changedFields);
            return result;
        }
    }

    internal static class ModelImporterPatchValue
    {
        internal static bool Same<T>(T left, T right) where T : struct
        {
            if (typeof(T) == typeof(float))
                return RestResponse.FormatFloat((float)(object)left) ==
                       RestResponse.FormatFloat((float)(object)right);
            return EqualityComparer<T>.Default.Equals(left, right);
        }
    }

    internal sealed class ModelImporterModelPatch
    {
        internal float? GlobalScale;
        internal bool? UseFileScale;
        internal bool? UseFileUnits;
        internal bool? BakeAxisConversion;
        internal bool? PreserveHierarchy;
        internal bool? IsReadable;

        internal void Apply(ModelImporterState state, List<string> changed)
        {
            ApplyValue(GlobalScale, state.GlobalScale, value => state.GlobalScale = value, "model.globalScale", changed);
            ApplyValue(UseFileScale, state.UseFileScale, value => state.UseFileScale = value, "model.useFileScale", changed);
            ApplyValue(UseFileUnits, state.UseFileUnits, value => state.UseFileUnits = value, "model.useFileUnits", changed);
            ApplyValue(BakeAxisConversion, state.BakeAxisConversion, value => state.BakeAxisConversion = value, "model.bakeAxisConversion", changed);
            ApplyValue(PreserveHierarchy, state.PreserveHierarchy, value => state.PreserveHierarchy = value, "model.preserveHierarchy", changed);
            ApplyValue(IsReadable, state.IsReadable, value => state.IsReadable = value, "model.isReadable", changed);
        }

        private static void ApplyValue<T>(T? requested, T current, Action<T> setter, string path, List<string> changed)
            where T : struct
        {
            if (!requested.HasValue) return;
            setter(requested.Value);
            if (!ModelImporterPatchValue.Same(requested.Value, current)) changed.Add(path);
        }
    }

    internal sealed class ModelImporterMeshPatch
    {
        internal ModelImporterMeshCompression? Compression;
        internal ModelImporterIndexFormat? IndexFormat;
        internal bool? KeepQuads;
        internal bool? WeldVertices;
        internal ModelImporterSkinWeights? SkinWeights;
        internal int? MaxBonesPerVertex;
        internal float? MinBoneWeight;
        internal bool? OptimizePolygons;
        internal bool? OptimizeVertices;

        internal void Apply(ModelImporterState state, List<string> changed)
        {
            Set(Compression, state.MeshCompression, value => state.MeshCompression = value, "mesh.compression", changed);
            Set(IndexFormat, state.IndexFormat, value => state.IndexFormat = value, "mesh.indexFormat", changed);
            Set(KeepQuads, state.KeepQuads, value => state.KeepQuads = value, "mesh.keepQuads", changed);
            Set(WeldVertices, state.WeldVertices, value => state.WeldVertices = value, "mesh.weldVertices", changed);
            Set(SkinWeights, state.SkinWeights, value => state.SkinWeights = value, "mesh.skinWeights", changed);
            Set(MaxBonesPerVertex, state.MaxBonesPerVertex, value => state.MaxBonesPerVertex = value, "mesh.maxBonesPerVertex", changed);
            Set(MinBoneWeight, state.MinBoneWeight, value => state.MinBoneWeight = value, "mesh.minBoneWeight", changed);
            Set(OptimizePolygons, state.OptimizeMeshPolygons, value => state.OptimizeMeshPolygons = value, "mesh.optimizePolygons", changed);
            Set(OptimizeVertices, state.OptimizeMeshVertices, value => state.OptimizeMeshVertices = value, "mesh.optimizeVertices", changed);
        }

        private static void Set<T>(T? requested, T current, Action<T> setter, string path, List<string> changed)
            where T : struct
        {
            if (!requested.HasValue) return;
            setter(requested.Value);
            if (!ModelImporterPatchValue.Same(requested.Value, current)) changed.Add(path);
        }
    }

    internal sealed class ModelImporterGeometryPatch
    {
        internal bool? AddCollider;
        internal bool? ImportBlendShapes;
        internal bool? ImportCameras;
        internal bool? ImportLights;
        internal bool? ImportVisibility;
        internal bool? ImportConstraints;
        internal bool? SwapUvChannels;
        internal bool? GenerateSecondaryUv;
        internal ModelImporterSecondaryUVMarginMethod? SecondaryUvMarginMethod;
        internal float? SecondaryUvAngleDistortion;
        internal float? SecondaryUvAreaDistortion;
        internal float? SecondaryUvHardAngle;
        internal float? SecondaryUvPackMargin;

        internal void Apply(ModelImporterState state, List<string> changed)
        {
            Set(AddCollider, state.AddCollider, value => state.AddCollider = value, "geometry.addCollider", changed);
            Set(ImportBlendShapes, state.ImportBlendShapes, value => state.ImportBlendShapes = value, "geometry.importBlendShapes", changed);
            Set(ImportCameras, state.ImportCameras, value => state.ImportCameras = value, "geometry.importCameras", changed);
            Set(ImportLights, state.ImportLights, value => state.ImportLights = value, "geometry.importLights", changed);
            Set(ImportVisibility, state.ImportVisibility, value => state.ImportVisibility = value, "geometry.importVisibility", changed);
            Set(ImportConstraints, state.ImportConstraints, value => state.ImportConstraints = value, "geometry.importConstraints", changed);
            Set(SwapUvChannels, state.SwapUvChannels, value => state.SwapUvChannels = value, "geometry.swapUvChannels", changed);
            Set(GenerateSecondaryUv, state.GenerateSecondaryUv, value => state.GenerateSecondaryUv = value, "geometry.generateSecondaryUv", changed);
            Set(SecondaryUvMarginMethod, state.SecondaryUvMarginMethod, value => state.SecondaryUvMarginMethod = value, "geometry.secondaryUvMarginMethod", changed);
            Set(SecondaryUvAngleDistortion, state.SecondaryUvAngleDistortion, value => state.SecondaryUvAngleDistortion = value, "geometry.secondaryUvAngleDistortion", changed);
            Set(SecondaryUvAreaDistortion, state.SecondaryUvAreaDistortion, value => state.SecondaryUvAreaDistortion = value, "geometry.secondaryUvAreaDistortion", changed);
            Set(SecondaryUvHardAngle, state.SecondaryUvHardAngle, value => state.SecondaryUvHardAngle = value, "geometry.secondaryUvHardAngle", changed);
            Set(SecondaryUvPackMargin, state.SecondaryUvPackMargin, value => state.SecondaryUvPackMargin = value, "geometry.secondaryUvPackMargin", changed);
        }

        private static void Set<T>(T? requested, T current, Action<T> setter, string path, List<string> changed)
            where T : struct
        {
            if (!requested.HasValue) return;
            setter(requested.Value);
            if (!ModelImporterPatchValue.Same(requested.Value, current)) changed.Add(path);
        }
    }

    internal sealed class ModelImporterNormalsPatch
    {
        internal ModelImporterNormals? Import;
        internal ModelImporterNormals? BlendShapeImport;
        internal ModelImporterNormalCalculationMode? CalculationMode;
        internal ModelImporterNormalSmoothingSource? SmoothingSource;
        internal float? SmoothingAngle;

        internal void Apply(ModelImporterState state, List<string> changed)
        {
            Set(Import, state.ImportNormals, value => state.ImportNormals = value, "normals.import", changed);
            Set(BlendShapeImport, state.ImportBlendShapeNormals, value => state.ImportBlendShapeNormals = value, "normals.blendShapeImport", changed);
            Set(CalculationMode, state.NormalCalculationMode, value => state.NormalCalculationMode = value, "normals.calculationMode", changed);
            Set(SmoothingSource, state.NormalSmoothingSource, value => state.NormalSmoothingSource = value, "normals.smoothingSource", changed);
            Set(SmoothingAngle, state.NormalSmoothingAngle, value => state.NormalSmoothingAngle = value, "normals.smoothingAngle", changed);
        }

        private static void Set<T>(T? requested, T current, Action<T> setter, string path, List<string> changed)
            where T : struct
        {
            if (!requested.HasValue) return;
            setter(requested.Value);
            if (!ModelImporterPatchValue.Same(requested.Value, current)) changed.Add(path);
        }
    }

    internal sealed class ModelImporterTangentsPatch
    {
        internal ModelImporterTangents? Import;

        internal void Apply(ModelImporterState state, List<string> changed)
        {
            if (!Import.HasValue) return;
            if (Import.Value != state.ImportTangents) changed.Add("tangents.import");
            state.ImportTangents = Import.Value;
        }
    }

    internal sealed class ModelImporterState
    {
        internal float GlobalScale;
        internal float FileScale;
        internal bool UseFileScale;
        internal bool UseFileUnits;
        internal bool BakeAxisConversion;
        internal bool PreserveHierarchy;
        internal bool IsReadable;
        internal ModelImporterMeshCompression MeshCompression;
        internal ModelImporterIndexFormat IndexFormat;
        internal bool KeepQuads;
        internal bool WeldVertices;
        internal ModelImporterSkinWeights SkinWeights;
        internal int MaxBonesPerVertex;
        internal float MinBoneWeight;
        internal bool OptimizeMeshPolygons;
        internal bool OptimizeMeshVertices;
        internal bool AddCollider;
        internal bool ImportBlendShapes;
        internal bool ImportCameras;
        internal bool ImportLights;
        internal bool ImportVisibility;
        internal bool ImportConstraints;
        internal bool SwapUvChannels;
        internal bool GenerateSecondaryUv;
        internal ModelImporterSecondaryUVMarginMethod SecondaryUvMarginMethod;
        internal float SecondaryUvAngleDistortion;
        internal float SecondaryUvAreaDistortion;
        internal float SecondaryUvHardAngle;
        internal float SecondaryUvPackMargin;
        internal ModelImporterNormals ImportNormals;
        internal ModelImporterNormals ImportBlendShapeNormals;
        internal ModelImporterNormalCalculationMode NormalCalculationMode;
        internal ModelImporterNormalSmoothingSource NormalSmoothingSource;
        internal float NormalSmoothingAngle;
        internal ModelImporterTangents ImportTangents;
        internal ModelImporterMaterialImportMode MaterialImportMode;
        internal ModelImporterMaterialLocation MaterialLocation;
        internal ModelImporterMaterialName MaterialName;
        internal ModelImporterMaterialSearch MaterialSearch;
        internal List<ModelImporterMaterialRemapState> MaterialRemaps;
        internal ModelImporterAnimationType AnimationType;
        internal ModelImporterAvatarSetup AvatarSetup;
        internal Avatar SourceAvatar;
        internal bool AutoGenerateAvatarMappingIfUnspecified;
        internal ModelImporterHumanoidOversampling HumanoidOversampling;
        internal bool OptimizeGameObjects;
        internal string[] ExtraExposedTransformPaths;
        internal ModelImporterClipAnimation[] StoredClipAnimations;
        internal ModelImporterClipAnimation[] DefaultClipAnimations;

        internal static ModelImporterState Capture(ModelImporter importer)
        {
            var state = new ModelImporterState
            {
                GlobalScale = importer.globalScale,
                FileScale = importer.fileScale,
                UseFileScale = importer.useFileScale,
                UseFileUnits = importer.useFileUnits,
                BakeAxisConversion = importer.bakeAxisConversion,
                PreserveHierarchy = importer.preserveHierarchy,
                IsReadable = importer.isReadable,
                MeshCompression = importer.meshCompression,
                IndexFormat = importer.indexFormat,
                KeepQuads = importer.keepQuads,
                WeldVertices = importer.weldVertices,
                SkinWeights = importer.skinWeights,
                MaxBonesPerVertex = importer.maxBonesPerVertex,
                MinBoneWeight = importer.minBoneWeight,
                OptimizeMeshPolygons = importer.optimizeMeshPolygons,
                OptimizeMeshVertices = importer.optimizeMeshVertices,
                AddCollider = importer.addCollider,
                ImportBlendShapes = importer.importBlendShapes,
                ImportCameras = importer.importCameras,
                ImportLights = importer.importLights,
                ImportVisibility = importer.importVisibility,
                ImportConstraints = importer.importConstraints,
                SwapUvChannels = importer.swapUVChannels,
                GenerateSecondaryUv = importer.generateSecondaryUV,
                SecondaryUvMarginMethod = importer.secondaryUVMarginMethod,
                SecondaryUvAngleDistortion = importer.secondaryUVAngleDistortion,
                SecondaryUvAreaDistortion = importer.secondaryUVAreaDistortion,
                SecondaryUvHardAngle = importer.secondaryUVHardAngle,
                SecondaryUvPackMargin = importer.secondaryUVPackMargin,
                ImportNormals = importer.importNormals,
                ImportBlendShapeNormals = importer.importBlendShapeNormals,
                NormalCalculationMode = importer.normalCalculationMode,
                NormalSmoothingSource = importer.normalSmoothingSource,
                NormalSmoothingAngle = importer.normalSmoothingAngle,
                ImportTangents = importer.importTangents
            };
            ModelImporterMaterialsRigState.Capture(importer, state);
            ModelImporterClipsState.Capture(importer, state);
            return state;
        }

        internal ModelImporterState Clone()
        {
            var clone = (ModelImporterState)MemberwiseClone();
            ModelImporterMaterialsRigState.CloneCollections(this, clone);
            ModelImporterClipsState.CloneCollections(this, clone);
            return clone;
        }

        internal void Apply(ModelImporter importer, ModelImporterUpdateRequest request = null)
        {
            importer.globalScale = GlobalScale;
            importer.useFileScale = UseFileScale;
            importer.useFileUnits = UseFileUnits;
            importer.bakeAxisConversion = BakeAxisConversion;
            importer.preserveHierarchy = PreserveHierarchy;
            importer.isReadable = IsReadable;
            importer.meshCompression = MeshCompression;
            importer.indexFormat = IndexFormat;
            importer.keepQuads = KeepQuads;
            importer.weldVertices = WeldVertices;
            importer.skinWeights = SkinWeights;
            importer.maxBonesPerVertex = MaxBonesPerVertex;
            importer.minBoneWeight = MinBoneWeight;
            importer.optimizeMeshPolygons = OptimizeMeshPolygons;
            importer.optimizeMeshVertices = OptimizeMeshVertices;
            importer.addCollider = AddCollider;
            importer.importBlendShapes = ImportBlendShapes;
            importer.importCameras = ImportCameras;
            importer.importLights = ImportLights;
            importer.importVisibility = ImportVisibility;
            importer.importConstraints = ImportConstraints;
            importer.swapUVChannels = SwapUvChannels;
            importer.generateSecondaryUV = GenerateSecondaryUv;
            importer.secondaryUVMarginMethod = SecondaryUvMarginMethod;
            importer.secondaryUVAngleDistortion = SecondaryUvAngleDistortion;
            importer.secondaryUVAreaDistortion = SecondaryUvAreaDistortion;
            importer.secondaryUVHardAngle = SecondaryUvHardAngle;
            importer.secondaryUVPackMargin = SecondaryUvPackMargin;
            importer.importNormals = ImportNormals;
            importer.importBlendShapeNormals = ImportBlendShapeNormals;
            importer.normalCalculationMode = NormalCalculationMode;
            importer.normalSmoothingSource = NormalSmoothingSource;
            importer.normalSmoothingAngle = NormalSmoothingAngle;
            importer.importTangents = ImportTangents;
            ModelImporterMaterialsRigState.Apply(importer, this, request);
            ModelImporterClipsState.Apply(importer, this, request);
        }

        internal bool EqualsState(ModelImporterState other)
        {
            if (other == null) return false;
            return Math.Abs(GlobalScale - other.GlobalScale) < 0.000001f &&
                   Math.Abs(FileScale - other.FileScale) < 0.000001f &&
                   UseFileScale == other.UseFileScale && UseFileUnits == other.UseFileUnits &&
                   BakeAxisConversion == other.BakeAxisConversion && PreserveHierarchy == other.PreserveHierarchy &&
                   IsReadable == other.IsReadable && MeshCompression == other.MeshCompression &&
                   IndexFormat == other.IndexFormat && KeepQuads == other.KeepQuads &&
                   WeldVertices == other.WeldVertices && SkinWeights == other.SkinWeights &&
                   MaxBonesPerVertex == other.MaxBonesPerVertex &&
                   Math.Abs(MinBoneWeight - other.MinBoneWeight) < 0.000001f &&
                   OptimizeMeshPolygons == other.OptimizeMeshPolygons &&
                   OptimizeMeshVertices == other.OptimizeMeshVertices && AddCollider == other.AddCollider &&
                   ImportBlendShapes == other.ImportBlendShapes && ImportCameras == other.ImportCameras &&
                   ImportLights == other.ImportLights && ImportVisibility == other.ImportVisibility &&
                   ImportConstraints == other.ImportConstraints && SwapUvChannels == other.SwapUvChannels &&
                   GenerateSecondaryUv == other.GenerateSecondaryUv &&
                   SecondaryUvMarginMethod == other.SecondaryUvMarginMethod &&
                   Math.Abs(SecondaryUvAngleDistortion - other.SecondaryUvAngleDistortion) < 0.000001f &&
                   Math.Abs(SecondaryUvAreaDistortion - other.SecondaryUvAreaDistortion) < 0.000001f &&
                   Math.Abs(SecondaryUvHardAngle - other.SecondaryUvHardAngle) < 0.000001f &&
                   Math.Abs(SecondaryUvPackMargin - other.SecondaryUvPackMargin) < 0.000001f &&
                   ImportNormals == other.ImportNormals && ImportBlendShapeNormals == other.ImportBlendShapeNormals &&
                   NormalCalculationMode == other.NormalCalculationMode &&
                   NormalSmoothingSource == other.NormalSmoothingSource &&
                   Math.Abs(NormalSmoothingAngle - other.NormalSmoothingAngle) < 0.000001f &&
                   ImportTangents == other.ImportTangents &&
                   ModelImporterMaterialsRigState.EqualsState(this, other) &&
                   ModelImporterClipsState.EqualsState(this, other);
        }
    }

    internal static class ModelImporterUpdateParser
    {
        private static readonly string[] TopFields =
            { "schemaVersion", "model", "mesh", "geometry", "normals", "tangents", "materials", "materialRemaps", "rig", "clips" };
        private static readonly string[] ModelFields =
            { "globalScale", "useFileScale", "useFileUnits", "bakeAxisConversion", "preserveHierarchy", "isReadable" };
        private static readonly string[] MeshFields =
            { "compression", "indexFormat", "keepQuads", "weldVertices", "skinWeights", "maxBonesPerVertex", "minBoneWeight", "optimizePolygons", "optimizeVertices" };
        private static readonly string[] GeometryFields =
        {
            "addCollider", "importBlendShapes", "importCameras", "importLights", "importVisibility",
            "importConstraints", "swapUvChannels", "generateSecondaryUv", "secondaryUvMarginMethod",
            "secondaryUvAngleDistortion", "secondaryUvAreaDistortion", "secondaryUvHardAngle", "secondaryUvPackMargin"
        };
        private static readonly string[] NormalsFields =
            { "import", "blendShapeImport", "calculationMode", "smoothingSource", "smoothingAngle" };
        private static readonly string[] TangentsFields = { "import" };

        internal static bool TryParse(string body, out ModelImporterUpdateRequest request, out string error)
        {
            request = null;
            if (!RequestBodyReader.TryValidateObjectFields(body, TopFields, out error)) return false;

            int schemaVersion;
            bool schemaPresent;
            if (!RequestBodyReader.TryGetIntValue(body, "schemaVersion", out schemaVersion, out schemaPresent) ||
                !schemaPresent || schemaVersion != ModelImporterUpdateRequest.SchemaVersion)
            {
                error = "'schemaVersion' must be the integer 1.";
                return false;
            }

            var parsed = new ModelImporterUpdateRequest();
            if (!TryParseModel(body, parsed, out error) ||
                !TryParseMesh(body, parsed, out error) ||
                !TryParseGeometry(body, parsed, out error) ||
                !TryParseNormals(body, parsed, out error) ||
                !TryParseTangents(body, parsed, out error) ||
                !ModelImporterMaterialsRigParser.TryParse(body, parsed, out error) ||
                !ModelImporterClipsParser.TryParse(body, parsed, out error))
                return false;

            if (!parsed.HasAnySetting)
            {
                error = "Request does not contain a ModelImporter setting to update.";
                return false;
            }

            request = parsed;
            error = null;
            return true;
        }

        internal static bool TryValidateFinalState(
            ModelImporterState state,
            ModelImporter importer,
            ModelImporterUpdateRequest request,
            out string error)
        {
            if (request.Model?.GlobalScale.HasValue == true &&
                (state.GlobalScale <= 0f || state.GlobalScale > 100000f))
            {
                error = "model.globalScale must be greater than 0 and at most 100000.";
                return false;
            }
            if (request.Model?.UseFileUnits == true && !importer.isUseFileUnitsSupported)
            {
                error = "model.useFileUnits is not supported by this model source.";
                return false;
            }
            if (request.Mesh?.MaxBonesPerVertex.HasValue == true &&
                (state.MaxBonesPerVertex < 1 || state.MaxBonesPerVertex > 255))
            {
                error = "mesh.maxBonesPerVertex must be between 1 and 255.";
                return false;
            }
            if (request.Mesh?.MinBoneWeight.HasValue == true &&
                (state.MinBoneWeight < 0f || state.MinBoneWeight > 1f))
            {
                error = "mesh.minBoneWeight must be between 0 and 1.";
                return false;
            }
            if (request.Geometry?.SecondaryUvAngleDistortion.HasValue == true &&
                !Between(state.SecondaryUvAngleDistortion, 1f, 75f))
            {
                error = "geometry.secondaryUvAngleDistortion must be between 1 and 75.";
                return false;
            }
            if (request.Geometry?.SecondaryUvAreaDistortion.HasValue == true &&
                !Between(state.SecondaryUvAreaDistortion, 1f, 75f))
            {
                error = "geometry.secondaryUvAreaDistortion must be between 1 and 75.";
                return false;
            }
            if (request.Geometry?.SecondaryUvHardAngle.HasValue == true &&
                !Between(state.SecondaryUvHardAngle, 0f, 180f))
            {
                error = "geometry.secondaryUvHardAngle must be between 0 and 180.";
                return false;
            }
            if (request.Geometry?.SecondaryUvPackMargin.HasValue == true &&
                !Between(state.SecondaryUvPackMargin, 1f, 64f))
            {
                error = "geometry.secondaryUvPackMargin must be between 1 and 64.";
                return false;
            }
            if (request.Normals?.SmoothingAngle.HasValue == true &&
                !Between(state.NormalSmoothingAngle, 0f, 180f))
            {
                error = "normals.smoothingAngle must be between 0 and 180.";
                return false;
            }
            if ((request.Normals?.Import.HasValue == true || request.Tangents?.Import.HasValue == true) &&
                state.ImportNormals == ModelImporterNormals.None &&
                state.ImportTangents != ModelImporterTangents.None)
            {
                error = "tangents.import must be None when normals.import is None.";
                return false;
            }
            if (request.Tangents?.Import.HasValue == true &&
                state.ImportTangents != ModelImporterTangents.None && !importer.isTangentImportSupported)
            {
                error = "Tangent import is not supported by this model source.";
                return false;
            }

            if (!ModelImporterMaterialsRigRules.TryValidate(state, importer, request, out error)) return false;
            return ModelImporterClipsRules.TryValidate(state, request, out error);
        }

        private static bool TryParseModel(string body, ModelImporterUpdateRequest request, out string error)
        {
            string json;
            if (!TryGetPatchObject(body, "model", ModelFields, out json, out error)) return false;
            if (json == null) return true;
            var patch = new ModelImporterModelPatch();
            if (!ReadFloat(json, "globalScale", "model.globalScale", out patch.GlobalScale, out error) ||
                !ReadBool(json, "useFileScale", "model.useFileScale", out patch.UseFileScale, out error) ||
                !ReadBool(json, "useFileUnits", "model.useFileUnits", out patch.UseFileUnits, out error) ||
                !ReadBool(json, "bakeAxisConversion", "model.bakeAxisConversion", out patch.BakeAxisConversion, out error) ||
                !ReadBool(json, "preserveHierarchy", "model.preserveHierarchy", out patch.PreserveHierarchy, out error) ||
                !ReadBool(json, "isReadable", "model.isReadable", out patch.IsReadable, out error)) return false;
            request.Model = patch;
            return true;
        }

        private static bool TryParseMesh(string body, ModelImporterUpdateRequest request, out string error)
        {
            string json;
            if (!TryGetPatchObject(body, "mesh", MeshFields, out json, out error)) return false;
            if (json == null) return true;
            var patch = new ModelImporterMeshPatch();
            if (!ReadEnum(json, "compression", "mesh.compression", out patch.Compression, out error) ||
                !ReadEnum(json, "indexFormat", "mesh.indexFormat", out patch.IndexFormat, out error) ||
                !ReadBool(json, "keepQuads", "mesh.keepQuads", out patch.KeepQuads, out error) ||
                !ReadBool(json, "weldVertices", "mesh.weldVertices", out patch.WeldVertices, out error) ||
                !ReadEnum(json, "skinWeights", "mesh.skinWeights", out patch.SkinWeights, out error) ||
                !ReadInt(json, "maxBonesPerVertex", "mesh.maxBonesPerVertex", out patch.MaxBonesPerVertex, out error) ||
                !ReadFloat(json, "minBoneWeight", "mesh.minBoneWeight", out patch.MinBoneWeight, out error) ||
                !ReadBool(json, "optimizePolygons", "mesh.optimizePolygons", out patch.OptimizePolygons, out error) ||
                !ReadBool(json, "optimizeVertices", "mesh.optimizeVertices", out patch.OptimizeVertices, out error)) return false;
            request.Mesh = patch;
            return true;
        }

        private static bool TryParseGeometry(string body, ModelImporterUpdateRequest request, out string error)
        {
            string json;
            if (!TryGetPatchObject(body, "geometry", GeometryFields, out json, out error)) return false;
            if (json == null) return true;
            var patch = new ModelImporterGeometryPatch();
            if (!ReadBool(json, "addCollider", "geometry.addCollider", out patch.AddCollider, out error) ||
                !ReadBool(json, "importBlendShapes", "geometry.importBlendShapes", out patch.ImportBlendShapes, out error) ||
                !ReadBool(json, "importCameras", "geometry.importCameras", out patch.ImportCameras, out error) ||
                !ReadBool(json, "importLights", "geometry.importLights", out patch.ImportLights, out error) ||
                !ReadBool(json, "importVisibility", "geometry.importVisibility", out patch.ImportVisibility, out error) ||
                !ReadBool(json, "importConstraints", "geometry.importConstraints", out patch.ImportConstraints, out error) ||
                !ReadBool(json, "swapUvChannels", "geometry.swapUvChannels", out patch.SwapUvChannels, out error) ||
                !ReadBool(json, "generateSecondaryUv", "geometry.generateSecondaryUv", out patch.GenerateSecondaryUv, out error) ||
                !ReadEnum(json, "secondaryUvMarginMethod", "geometry.secondaryUvMarginMethod", out patch.SecondaryUvMarginMethod, out error) ||
                !ReadFloat(json, "secondaryUvAngleDistortion", "geometry.secondaryUvAngleDistortion", out patch.SecondaryUvAngleDistortion, out error) ||
                !ReadFloat(json, "secondaryUvAreaDistortion", "geometry.secondaryUvAreaDistortion", out patch.SecondaryUvAreaDistortion, out error) ||
                !ReadFloat(json, "secondaryUvHardAngle", "geometry.secondaryUvHardAngle", out patch.SecondaryUvHardAngle, out error) ||
                !ReadFloat(json, "secondaryUvPackMargin", "geometry.secondaryUvPackMargin", out patch.SecondaryUvPackMargin, out error)) return false;
            request.Geometry = patch;
            return true;
        }

        private static bool TryParseNormals(string body, ModelImporterUpdateRequest request, out string error)
        {
            string json;
            if (!TryGetPatchObject(body, "normals", NormalsFields, out json, out error)) return false;
            if (json == null) return true;
            var patch = new ModelImporterNormalsPatch();
            if (!ReadEnum(json, "import", "normals.import", out patch.Import, out error) ||
                !ReadEnum(json, "blendShapeImport", "normals.blendShapeImport", out patch.BlendShapeImport, out error) ||
                !ReadEnum(json, "calculationMode", "normals.calculationMode", out patch.CalculationMode, out error) ||
                !ReadEnum(json, "smoothingSource", "normals.smoothingSource", out patch.SmoothingSource, out error) ||
                !ReadFloat(json, "smoothingAngle", "normals.smoothingAngle", out patch.SmoothingAngle, out error)) return false;
            request.Normals = patch;
            return true;
        }

        private static bool TryParseTangents(string body, ModelImporterUpdateRequest request, out string error)
        {
            string json;
            if (!TryGetPatchObject(body, "tangents", TangentsFields, out json, out error)) return false;
            if (json == null) return true;
            var patch = new ModelImporterTangentsPatch();
            if (!ReadEnum(json, "import", "tangents.import", out patch.Import, out error)) return false;
            request.Tangents = patch;
            return true;
        }

        private static bool TryGetPatchObject(
            string body, string key, string[] fields, out string json, out string error)
        {
            json = null;
            error = null;
            if (!RequestBodyReader.HasTopLevelField(body, key)) return true;
            json = RequestBodyReader.GetObject(body, key);
            if (json == null)
            {
                error = "'" + key + "' must be a JSON object.";
                return false;
            }
            if (!RequestBodyReader.TryValidateObjectFields(json, fields, out error))
            {
                error = key + ": " + error;
                return false;
            }
            List<string> names;
            if (!RequestBodyReader.TryGetTopLevelFieldNames(json, out names, out error)) return false;
            if (names.Count == 0)
            {
                error = "'" + key + "' must contain at least one setting.";
                return false;
            }
            return true;
        }

        private static bool ReadBool(string json, string key, string path, out bool? value, out string error)
        {
            value = null;
            bool parsed;
            bool present;
            if (!RequestBodyReader.TryGetBoolValue(json, key, out parsed, out present))
            {
                error = path + " must be a JSON boolean.";
                return false;
            }
            if (present) value = parsed;
            error = null;
            return true;
        }

        private static bool ReadInt(string json, string key, string path, out int? value, out string error)
        {
            value = null;
            int parsed;
            bool present;
            if (!RequestBodyReader.TryGetIntValue(json, key, out parsed, out present))
            {
                error = path + " must be a JSON integer.";
                return false;
            }
            if (present) value = parsed;
            error = null;
            return true;
        }

        private static bool ReadFloat(string json, string key, string path, out float? value, out string error)
        {
            value = null;
            float parsed;
            bool present;
            if (!RequestBodyReader.TryGetFloatValue(json, key, out parsed, out present))
            {
                error = path + " must be a finite JSON number.";
                return false;
            }
            if (present) value = parsed;
            error = null;
            return true;
        }

        private static bool ReadEnum<T>(string json, string key, string path, out T? value, out string error)
            where T : struct
        {
            value = null;
            string text;
            bool present;
            if (!RequestBodyReader.TryGetStringValue(json, key, out text, out present))
            {
                error = path + " must be a JSON string.";
                return false;
            }
            if (!present)
            {
                error = null;
                return true;
            }
            T parsed;
            if (!Enum.TryParse(text, true, out parsed) || !Enum.IsDefined(typeof(T), parsed))
            {
                error = path + " must be one of: " + string.Join(", ", Enum.GetNames(typeof(T))) + ".";
                return false;
            }
            value = parsed;
            error = null;
            return true;
        }

        private static bool Between(float value, float minimum, float maximum)
            => !float.IsNaN(value) && !float.IsInfinity(value) && value >= minimum && value <= maximum;
    }
}
