using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEngine;

namespace LeonAkasaka.UnionAir.Editor
{
    internal sealed class ModelImporterHandler
    {
        private readonly Action<ModelImporter> _saveAndReimport;

        internal ModelImporterHandler(Action<ModelImporter> saveAndReimport = null)
        {
            _saveAndReimport = saveAndReimport ?? (value => value.SaveAndReimport());
        }

        internal void HandleGet(UnionAirResponse response, string guid)
        {
            string assetPath;
            ModelImporter importer;
            if (!TryResolve(guid, response, out assetPath, out importer)) return;
            RestResponse.Send(response, ModelImporterJson.BuildGet(guid, assetPath, importer));
        }

        internal void HandlePreflight(UnionAirRequest request, UnionAirResponse response, string guid)
        {
            string assetPath;
            ModelImporter importer;
            if (!TryResolve(guid, response, out assetPath, out importer)) return;

            ModelImporterState before;
            ModelImporterState requested;
            List<string> changed;
            ModelImporterUpdateRequest update;
            if (!TryPrepare(request, response, importer, out before, out requested, out changed, out update)) return;

            RestResponse.Send(
                response,
                ModelImporterJson.BuildPreflight(guid, assetPath, importer, before, requested, changed));
        }

        internal void HandleUpdate(UnionAirRequest request, UnionAirResponse response, string guid)
        {
            string assetPath;
            ModelImporter importer;
            if (!TryResolve(guid, response, out assetPath, out importer)) return;

            ModelImporterState before;
            ModelImporterState requested;
            List<string> changed;
            ModelImporterUpdateRequest update;
            if (!TryPrepare(request, response, importer, out before, out requested, out changed, out update)) return;

            var beforeSubAssets = ModelImporterSubAssets.Capture(guid, assetPath);
            if (changed.Count == 0)
            {
                RestResponse.Send(
                    response,
                    ModelImporterJson.BuildUpdate(
                        guid, assetPath, importer, before, before, beforeSubAssets, beforeSubAssets,
                        changed, false, null, null));
                return;
            }

            if (!AssetDatabase.IsOpenForEdit(assetPath))
            {
                RestResponse.SendError(
                    response,
                    "The model importer is not open for edit: " + assetPath,
                    409);
                return;
            }
            if (LoadedSceneDiskChangeGuard.SendConflictIfAny(response)) return;

            try
            {
                requested.Apply(importer, update);
                _saveAndReimport(importer);
            }
            catch (Exception ex)
            {
                string rollbackError;
                var restored = TryRestore(assetPath, importer, before, update, out rollbackError);
                RestResponse.Send(
                    response,
                    ModelImporterJson.BuildFailure(
                        guid, assetPath, ex.Message, before, changed, restored, rollbackError),
                    500);
                return;
            }

            var finalImporter = AssetImporter.GetAtPath(assetPath) as ModelImporter;
            if (finalImporter == null)
            {
                RestResponse.SendError(
                    response,
                    "The reimported asset no longer has a ModelImporter.",
                    500);
                return;
            }

            var after = ModelImporterState.Capture(finalImporter);
            var afterSubAssets = ModelImporterSubAssets.Capture(guid, assetPath);
            RestResponse.Send(
                response,
                ModelImporterJson.BuildUpdate(
                    guid, assetPath, finalImporter, before, after, beforeSubAssets, afterSubAssets,
                    changed, true, AssetImporter.GetImportLog(assetPath), null));
        }

        private static bool TryPrepare(
            UnionAirRequest request,
            UnionAirResponse response,
            ModelImporter importer,
            out ModelImporterState before,
            out ModelImporterState requested,
            out List<string> changed,
            out ModelImporterUpdateRequest update)
        {
            before = null;
            requested = null;
            changed = null;
            update = null;

            string error;
            if (!ModelImporterUpdateParser.TryParse(RequestBodyReader.ReadString(request), out update, out error))
            {
                RestResponse.SendError(response, error, 400);
                return false;
            }
            if (!ModelImporterMaterialsRigParser.TryResolveReferences(update, out error))
            {
                RestResponse.SendError(response, error, 400);
                return false;
            }
            if (!ModelImporterClipsParser.TryResolveReferences(update, out error))
            {
                RestResponse.SendError(response, error, 400);
                return false;
            }

            before = ModelImporterState.Capture(importer);
            if (!ModelImporterClipsParser.TryPrepare(update, before, out error))
            {
                RestResponse.SendError(response, error, 400);
                return false;
            }
            changed = new List<string>();
            requested = update.Apply(before, changed);
            if (!ModelImporterUpdateParser.TryValidateFinalState(requested, importer, update, out error))
            {
                RestResponse.SendError(response, error, 400);
                return false;
            }

            return true;
        }

        private static bool TryResolve(
            string guid,
            UnionAirResponse response,
            out string assetPath,
            out ModelImporter importer)
        {
            assetPath = string.IsNullOrEmpty(guid) ? string.Empty : AssetDatabase.GUIDToAssetPath(guid);
            importer = null;
            if (string.IsNullOrEmpty(assetPath))
            {
                RestResponse.SendNotFound(response, "No asset found for GUID: " + (guid ?? string.Empty));
                return false;
            }

            importer = AssetImporter.GetAtPath(assetPath) as ModelImporter;
            if (importer != null) return true;

            RestResponse.SendError(response, "Asset does not use a ModelImporter: " + assetPath, 400);
            return false;
        }

        private static bool TryRestore(
            string assetPath,
            ModelImporter importer,
            ModelImporterState before,
            ModelImporterUpdateRequest update,
            out string error)
        {
            try
            {
                var rollbackImporter = AssetImporter.GetAtPath(assetPath) as ModelImporter ?? importer;
                if (rollbackImporter == null)
                {
                    error = "The asset no longer has a ModelImporter.";
                    return false;
                }

                before.Apply(rollbackImporter, update);
                var restored = ModelImporterState.Capture(rollbackImporter);
                if (!before.EqualsState(restored))
                {
                    error = "Unity did not restore the original exposed ModelImporter settings.";
                    return false;
                }

                AssetDatabase.WriteImportSettingsIfDirty(assetPath);
                error = null;
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }
    }

    internal sealed class ModelImporterSubAsset
    {
        internal string Guid;
        internal string LocalIdentifier;
        internal string Name;
        internal string Type;

        internal string Key => Guid + ":" + LocalIdentifier + ":" + Type;
    }

    internal static class ModelImporterSubAssets
    {
        internal static List<ModelImporterSubAsset> Capture(string guid, string assetPath)
        {
            var result = new List<ModelImporterSubAsset>();
            foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(assetPath))
            {
                if (asset == null || !AssetDatabase.IsSubAsset(asset) || !IsPublishedType(asset)) continue;
                var id = GlobalObjectId.GetGlobalObjectIdSlow(asset);
                result.Add(new ModelImporterSubAsset
                {
                    Guid = guid,
                    LocalIdentifier = id.targetObjectId.ToString(CultureInfo.InvariantCulture),
                    Name = asset.name,
                    Type = asset.GetType().FullName
                });
            }

            result.Sort((a, b) =>
            {
                var type = string.Compare(a.Type, b.Type, StringComparison.Ordinal);
                if (type != 0) return type;
                var name = string.Compare(a.Name, b.Name, StringComparison.Ordinal);
                if (name != 0) return name;
                return string.Compare(a.LocalIdentifier, b.LocalIdentifier, StringComparison.Ordinal);
            });
            return result;
        }

        internal static void Diff(
            List<ModelImporterSubAsset> before,
            List<ModelImporterSubAsset> after,
            out List<ModelImporterSubAsset> added,
            out List<ModelImporterSubAsset> removed)
        {
            var beforeKeys = new HashSet<string>(StringComparer.Ordinal);
            var afterKeys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in before) beforeKeys.Add(item.Key);
            foreach (var item in after) afterKeys.Add(item.Key);

            added = new List<ModelImporterSubAsset>();
            removed = new List<ModelImporterSubAsset>();
            foreach (var item in after) if (!beforeKeys.Contains(item.Key)) added.Add(item);
            foreach (var item in before) if (!afterKeys.Contains(item.Key)) removed.Add(item);
        }

        private static bool IsPublishedType(UnityEngine.Object asset)
            => asset is Mesh || asset is Material || asset is Avatar || asset is AnimationClip;
    }

    internal static class ModelImporterJson
    {
        internal static string BuildGet(string guid, string assetPath, ModelImporter importer)
        {
            var sb = Begin(guid, assetPath);
            AppendCapabilities(sb, importer);
            sb.Append(",\"settings\":");
            AppendSettings(sb, ModelImporterState.Capture(importer));
            sb.Append(",\"subAssets\":");
            AppendSubAssets(sb, ModelImporterSubAssets.Capture(guid, assetPath));
            sb.Append('}');
            return sb.ToString();
        }

        internal static string BuildPreflight(
            string guid,
            string assetPath,
            ModelImporter importer,
            ModelImporterState before,
            ModelImporterState requested,
            List<string> changed)
        {
            var sb = Begin(guid, assetPath);
            AppendCapabilities(sb, importer);
            sb.Append(",\"valid\":true,\"reimportRequired\":").Append(Bool(changed.Count > 0));
            sb.Append(",\"changedFields\":");
            AppendStrings(sb, changed);
            sb.Append(",\"before\":");
            AppendSettings(sb, before);
            sb.Append(",\"after\":");
            AppendSettings(sb, requested);
            sb.Append('}');
            return sb.ToString();
        }

        internal static string BuildUpdate(
            string guid,
            string assetPath,
            ModelImporter importer,
            ModelImporterState before,
            ModelImporterState after,
            List<ModelImporterSubAsset> beforeSubAssets,
            List<ModelImporterSubAsset> afterSubAssets,
            List<string> changed,
            bool reimported,
            ImportLog log,
            string rollback)
        {
            var sb = Begin(guid, assetPath);
            AppendCapabilities(sb, importer);
            sb.Append(",\"reimported\":").Append(Bool(reimported));
            sb.Append(",\"changedFields\":");
            AppendStrings(sb, changed);
            sb.Append(",\"before\":{\"settings\":");
            AppendSettings(sb, before);
            sb.Append(",\"subAssets\":");
            AppendSubAssets(sb, beforeSubAssets);
            sb.Append("},\"after\":{\"settings\":");
            AppendSettings(sb, after);
            sb.Append(",\"subAssets\":");
            AppendSubAssets(sb, afterSubAssets);
            sb.Append("},\"subAssetDelta\":");
            AppendDelta(sb, beforeSubAssets, afterSubAssets);
            sb.Append(",\"diagnostics\":");
            AppendDiagnostics(sb, log);
            sb.Append(",\"rollback\":").Append(rollback ?? "null");
            sb.Append('}');
            return sb.ToString();
        }

        internal static string BuildFailure(
            string guid,
            string assetPath,
            string message,
            ModelImporterState before,
            List<string> changed,
            bool restored,
            string rollbackError)
        {
            var sb = Begin(guid, assetPath);
            sb.Append(",\"error\":").Append(RestResponse.FormatNullableString("Model reimport failed: " + message));
            sb.Append(",\"reimported\":false,\"changedFields\":");
            AppendStrings(sb, changed);
            sb.Append(",\"before\":");
            AppendSettings(sb, before);
            sb.Append(",\"rollback\":{\"attempted\":true,\"restored\":").Append(Bool(restored));
            sb.Append(",\"error\":").Append(RestResponse.FormatNullableString(rollbackError));
            sb.Append("}}");
            return sb.ToString();
        }

        private static StringBuilder Begin(string guid, string assetPath)
        {
            var sb = new StringBuilder(8192);
            sb.Append("{\"schemaVersion\":1,\"guid\":")
              .Append(RestResponse.FormatNullableString(guid))
              .Append(",\"assetPath\":")
              .Append(RestResponse.FormatNullableString(assetPath));
            return sb;
        }

        private static void AppendCapabilities(StringBuilder sb, ModelImporter importer)
        {
            sb.Append(",\"capabilities\":{\"unityVersion\":")
              .Append(RestResponse.FormatNullableString(Application.unityVersion))
              .Append(",\"useFileUnits\":").Append(Bool(importer.isUseFileUnitsSupported))
              .Append(",\"tangentImport\":").Append(Bool(importer.isTangentImportSupported))
              .Append(",\"bakeIk\":").Append(Bool(importer.isBakeIKSupported))
              .Append(",\"settings\":{\"model.useFileUnits\":")
              .Append(Bool(importer.isUseFileUnitsSupported))
              .Append(",\"tangents.import\":").Append(Bool(importer.isTangentImportSupported))
              .Append(",\"clips.definitions\":true,\"clips.avatarMask\":true,\"clips.events\":true")
              .Append(",\"clips.curves\":false")
              .Append(",\"rig.humanDescription\":false}")
              .Append(",\"unavailableSettings\":[");
            var separator = false;
            if (!importer.isUseFileUnitsSupported)
            {
                sb.Append("\"model.useFileUnits\"");
                separator = true;
            }
            if (!importer.isTangentImportSupported)
            {
                if (separator) sb.Append(',');
                sb.Append("\"tangents.import\"");
                separator = true;
            }
            if (separator) sb.Append(',');
            sb.Append("\"rig.humanDescription\",\"clips.curves\"]}");
        }

        internal static void AppendSettings(StringBuilder sb, ModelImporterState state)
        {
            sb.Append("{\"model\":{\"globalScale\":").Append(RestResponse.FormatFloat(state.GlobalScale))
              .Append(",\"fileScale\":").Append(RestResponse.FormatFloat(state.FileScale))
              .Append(",\"useFileScale\":").Append(Bool(state.UseFileScale))
              .Append(",\"useFileUnits\":").Append(Bool(state.UseFileUnits))
              .Append(",\"bakeAxisConversion\":").Append(Bool(state.BakeAxisConversion))
              .Append(",\"preserveHierarchy\":").Append(Bool(state.PreserveHierarchy))
              .Append(",\"isReadable\":").Append(Bool(state.IsReadable)).Append("}");
            sb.Append(",\"mesh\":{\"compression\":").Append(Q(state.MeshCompression))
              .Append(",\"indexFormat\":").Append(Q(state.IndexFormat))
              .Append(",\"keepQuads\":").Append(Bool(state.KeepQuads))
              .Append(",\"weldVertices\":").Append(Bool(state.WeldVertices))
              .Append(",\"skinWeights\":").Append(Q(state.SkinWeights))
              .Append(",\"maxBonesPerVertex\":").Append(state.MaxBonesPerVertex)
              .Append(",\"minBoneWeight\":").Append(RestResponse.FormatFloat(state.MinBoneWeight))
              .Append(",\"optimizePolygons\":").Append(Bool(state.OptimizeMeshPolygons))
              .Append(",\"optimizeVertices\":").Append(Bool(state.OptimizeMeshVertices)).Append("}");
            sb.Append(",\"geometry\":{\"addCollider\":").Append(Bool(state.AddCollider))
              .Append(",\"importBlendShapes\":").Append(Bool(state.ImportBlendShapes))
              .Append(",\"importCameras\":").Append(Bool(state.ImportCameras))
              .Append(",\"importLights\":").Append(Bool(state.ImportLights))
              .Append(",\"importVisibility\":").Append(Bool(state.ImportVisibility))
              .Append(",\"importConstraints\":").Append(Bool(state.ImportConstraints))
              .Append(",\"swapUvChannels\":").Append(Bool(state.SwapUvChannels))
              .Append(",\"generateSecondaryUv\":").Append(Bool(state.GenerateSecondaryUv))
              .Append(",\"secondaryUvMarginMethod\":").Append(Q(state.SecondaryUvMarginMethod))
              .Append(",\"secondaryUvAngleDistortion\":").Append(RestResponse.FormatFloat(state.SecondaryUvAngleDistortion))
              .Append(",\"secondaryUvAreaDistortion\":").Append(RestResponse.FormatFloat(state.SecondaryUvAreaDistortion))
              .Append(",\"secondaryUvHardAngle\":").Append(RestResponse.FormatFloat(state.SecondaryUvHardAngle))
              .Append(",\"secondaryUvPackMargin\":").Append(RestResponse.FormatFloat(state.SecondaryUvPackMargin)).Append("}");
            sb.Append(",\"normals\":{\"import\":").Append(Q(state.ImportNormals))
              .Append(",\"blendShapeImport\":").Append(Q(state.ImportBlendShapeNormals))
              .Append(",\"calculationMode\":").Append(Q(state.NormalCalculationMode))
              .Append(",\"smoothingSource\":").Append(Q(state.NormalSmoothingSource))
              .Append(",\"smoothingAngle\":").Append(RestResponse.FormatFloat(state.NormalSmoothingAngle)).Append("}");
            sb.Append(",\"tangents\":{\"import\":").Append(Q(state.ImportTangents)).Append("}");
            sb.Append(",\"materials\":{\"importMode\":").Append(Q(state.MaterialImportMode))
              .Append(",\"location\":").Append(Q(state.MaterialLocation))
              .Append(",\"naming\":").Append(Q(state.MaterialName))
              .Append(",\"search\":").Append(Q(state.MaterialSearch)).Append("}");
            sb.Append(",\"materialRemaps\":");
            AppendMaterialRemaps(sb, state.MaterialRemaps);
            sb.Append(",\"rig\":{\"animationType\":").Append(Q(state.AnimationType))
              .Append(",\"avatarSetup\":").Append(Q(state.AvatarSetup))
              .Append(",\"sourceAvatar\":");
            AppendObjectReference(sb, state.SourceAvatar);
            sb.Append(",\"autoGenerateAvatarMappingIfUnspecified\":")
              .Append(Bool(state.AutoGenerateAvatarMappingIfUnspecified))
              .Append(",\"humanoidOversampling\":").Append(Q(state.HumanoidOversampling))
              .Append(",\"optimizeGameObjects\":").Append(Bool(state.OptimizeGameObjects))
              .Append(",\"extraExposedTransformPaths\":");
            AppendStringArray(sb, state.ExtraExposedTransformPaths);
            sb.Append("},\"clips\":");
            AppendClips(sb, state);
            sb.Append(",\"unsupportedInitialSettings\":[\"rig.humanDescription\",\"clips.curves\"]}");
        }

        private static void AppendClips(StringBuilder sb, ModelImporterState state)
        {
            sb.Append("{\"derivedFromDefaults\":")
              .Append(Bool(ModelImporterClipsState.DerivedFromDefaults(state)))
              .Append(",\"definitions\":[");
            var clips = ModelImporterClipsState.Effective(state);
            for (var i = 0; i < clips.Length; i++)
            {
                if (i > 0) sb.Append(',');
                var clip = clips[i];
                sb.Append("{\"takeName\":").Append(RestResponse.FormatNullableString(clip.takeName))
                  .Append(",\"name\":").Append(RestResponse.FormatNullableString(clip.name))
                  .Append(",\"firstFrame\":").Append(RestResponse.FormatFloat(clip.firstFrame))
                  .Append(",\"lastFrame\":").Append(RestResponse.FormatFloat(clip.lastFrame))
                  .Append(",\"wrapMode\":").Append(Q(clip.wrapMode))
                  .Append(",\"loop\":").Append(Bool(clip.loop))
                  .Append(",\"loopTime\":").Append(Bool(clip.loopTime))
                  .Append(",\"loopPose\":").Append(Bool(clip.loopPose))
                  .Append(",\"mirror\":").Append(Bool(clip.mirror))
                  .Append(",\"lockRootRotation\":").Append(Bool(clip.lockRootRotation))
                  .Append(",\"keepOriginalOrientation\":").Append(Bool(clip.keepOriginalOrientation))
                  .Append(",\"rotationOffset\":").Append(RestResponse.FormatFloat(clip.rotationOffset))
                  .Append(",\"lockRootHeightY\":").Append(Bool(clip.lockRootHeightY))
                  .Append(",\"keepOriginalPositionY\":").Append(Bool(clip.keepOriginalPositionY))
                  .Append(",\"heightFromFeet\":").Append(Bool(clip.heightFromFeet))
                  .Append(",\"heightOffset\":").Append(RestResponse.FormatFloat(clip.heightOffset))
                  .Append(",\"lockRootPositionXZ\":").Append(Bool(clip.lockRootPositionXZ))
                  .Append(",\"keepOriginalPositionXZ\":").Append(Bool(clip.keepOriginalPositionXZ))
                  .Append(",\"cycleOffset\":").Append(RestResponse.FormatFloat(clip.cycleOffset))
                  .Append(",\"hasAdditiveReferencePose\":").Append(Bool(clip.hasAdditiveReferencePose))
                  .Append(",\"additiveReferencePoseFrame\":")
                  .Append(RestResponse.FormatFloat(clip.additiveReferencePoseFrame))
                  .Append(",\"maskType\":").Append(Q(clip.maskType))
                  .Append(",\"maskSource\":");
                AppendObjectReference(sb, clip.maskSource);
                sb.Append(",\"maskNeedsUpdating\":").Append(Bool(clip.maskNeedsUpdating))
                  .Append(",\"events\":");
                AnimationEventJson.Append(sb, clip.events ?? new AnimationEvent[0]);
                sb.Append('}');
            }
            sb.Append("]}");
        }

        private static void AppendMaterialRemaps(
            StringBuilder sb, List<ModelImporterMaterialRemapState> remaps)
        {
            sb.Append('[');
            if (remaps != null)
            {
                for (var i = 0; i < remaps.Count; i++)
                {
                    if (i > 0) sb.Append(',');
                    var remap = remaps[i];
                    sb.Append("{\"source\":{\"type\":")
                      .Append(RestResponse.FormatNullableString(remap.SourceType))
                      .Append(",\"name\":")
                      .Append(RestResponse.FormatNullableString(remap.SourceName))
                      .Append("},\"target\":");
                    AppendObjectReference(sb, remap.Target);
                    sb.Append('}');
                }
            }
            sb.Append(']');
        }

        private static void AppendObjectReference(StringBuilder sb, UnityEngine.Object asset)
        {
            if (asset == null)
            {
                sb.Append("null");
                return;
            }
            var path = AssetDatabase.GetAssetPath(asset);
            sb.Append("{\"guid\":")
              .Append(RestResponse.FormatNullableString(AssetDatabase.AssetPathToGUID(path)))
              .Append(",\"localIdentifier\":")
              .Append(RestResponse.FormatNullableString(ModelImporterObjectIdentity.LocalIdentifier(asset)))
              .Append(",\"name\":").Append(RestResponse.FormatNullableString(asset.name))
              .Append(",\"type\":").Append(RestResponse.FormatNullableString(asset.GetType().FullName))
              .Append('}');
        }

        private static void AppendStringArray(StringBuilder sb, string[] values)
        {
            sb.Append('[');
            if (values != null)
            {
                for (var i = 0; i < values.Length; i++)
                {
                    if (i > 0) sb.Append(',');
                    sb.Append(RestResponse.FormatNullableString(values[i]));
                }
            }
            sb.Append(']');
        }

        private static void AppendSubAssets(StringBuilder sb, List<ModelImporterSubAsset> assets)
        {
            sb.Append('[');
            for (var i = 0; i < assets.Count; i++)
            {
                if (i > 0) sb.Append(',');
                AppendSubAsset(sb, assets[i]);
            }
            sb.Append(']');
        }

        private static void AppendSubAsset(StringBuilder sb, ModelImporterSubAsset asset)
        {
            sb.Append("{\"guid\":").Append(RestResponse.FormatNullableString(asset.Guid))
              .Append(",\"localIdentifier\":").Append(RestResponse.FormatNullableString(asset.LocalIdentifier))
              .Append(",\"name\":").Append(RestResponse.FormatNullableString(asset.Name))
              .Append(",\"type\":").Append(RestResponse.FormatNullableString(asset.Type)).Append('}');
        }

        private static void AppendDelta(
            StringBuilder sb,
            List<ModelImporterSubAsset> before,
            List<ModelImporterSubAsset> after)
        {
            List<ModelImporterSubAsset> added;
            List<ModelImporterSubAsset> removed;
            ModelImporterSubAssets.Diff(before, after, out added, out removed);
            sb.Append("{\"added\":");
            AppendSubAssets(sb, added);
            sb.Append(",\"removed\":");
            AppendSubAssets(sb, removed);
            sb.Append('}');
        }

        private static void AppendDiagnostics(StringBuilder sb, ImportLog log)
        {
            sb.Append('[');
            var wrote = false;
            if (log != null && log.logEntries != null)
            {
                foreach (var entry in log.logEntries)
                {
                    var isError = (entry.flags & ImportLogFlags.Error) != 0;
                    var isWarning = (entry.flags & ImportLogFlags.Warning) != 0;
                    if (!isError && !isWarning) continue;
                    if (wrote) sb.Append(',');
                    wrote = true;
                    sb.Append("{\"severity\":").Append(RestResponse.FormatNullableString(isError ? "error" : "warning"))
                      .Append(",\"message\":").Append(RestResponse.FormatNullableString(entry.message))
                      .Append(",\"file\":").Append(RestResponse.FormatNullableString(entry.file ?? string.Empty))
                      .Append(",\"line\":").Append(entry.line).Append('}');
                }
            }
            sb.Append(']');
        }

        private static void AppendStrings(StringBuilder sb, List<string> values)
        {
            sb.Append('[');
            for (var i = 0; i < values.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(RestResponse.FormatNullableString(values[i]));
            }
            sb.Append(']');
        }

        private static string Q(object value) => RestResponse.FormatNullableString(value.ToString());
        private static string Bool(bool value) => value ? "true" : "false";
    }
}
