using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEditor;
using UnityEngine;

namespace LeonAkasaka.UnionAir.Editor
{
    internal sealed class ModelImporterMaterialsPatch
    {
        internal ModelImporterMaterialImportMode? ImportMode;
        internal ModelImporterMaterialLocation? Location;
        internal ModelImporterMaterialName? Naming;
        internal ModelImporterMaterialSearch? Search;

        internal void Apply(ModelImporterState state, List<string> changed)
        {
            Set(ImportMode, state.MaterialImportMode, value => state.MaterialImportMode = value,
                "materials.importMode", changed);
            Set(Location, state.MaterialLocation, value => state.MaterialLocation = value,
                "materials.location", changed);
            Set(Naming, state.MaterialName, value => state.MaterialName = value,
                "materials.naming", changed);
            Set(Search, state.MaterialSearch, value => state.MaterialSearch = value,
                "materials.search", changed);
        }

        private static void Set<T>(T? requested, T current, Action<T> setter, string path, List<string> changed)
            where T : struct
        {
            if (!requested.HasValue) return;
            setter(requested.Value);
            if (!EqualityComparer<T>.Default.Equals(requested.Value, current)) changed.Add(path);
        }
    }

    internal sealed class ModelImporterObjectReferenceRequest
    {
        internal string Guid;
        internal string LocalIdentifier;
        internal UnityEngine.Object Resolved;
    }

    internal sealed class ModelImporterMaterialRemapPatch
    {
        internal string SourceName;
        internal string SourceType;
        internal ModelImporterObjectReferenceRequest Target;

        internal string Key => SourceType + ":" + SourceName;
    }

    internal sealed class ModelImporterRigPatch
    {
        internal ModelImporterAnimationType? AnimationType;
        internal ModelImporterAvatarSetup? AvatarSetup;
        internal bool HasSourceAvatar;
        internal ModelImporterObjectReferenceRequest SourceAvatar;
        internal bool? AutoGenerateAvatarMappingIfUnspecified;
        internal ModelImporterHumanoidOversampling? HumanoidOversampling;
        internal bool? OptimizeGameObjects;
        internal bool HasExtraExposedTransformPaths;
        internal string[] ExtraExposedTransformPaths;

        internal void Apply(ModelImporterState state, List<string> changed)
        {
            Set(AnimationType, state.AnimationType, value => state.AnimationType = value,
                "rig.animationType", changed);
            Set(AvatarSetup, state.AvatarSetup, value => state.AvatarSetup = value,
                "rig.avatarSetup", changed);
            if (HasSourceAvatar)
            {
                var value = SourceAvatar?.Resolved as Avatar;
                if (!ModelImporterObjectIdentity.Same(state.SourceAvatar, value))
                    changed.Add("rig.sourceAvatar");
                state.SourceAvatar = value;
            }
            Set(AutoGenerateAvatarMappingIfUnspecified, state.AutoGenerateAvatarMappingIfUnspecified,
                value => state.AutoGenerateAvatarMappingIfUnspecified = value,
                "rig.autoGenerateAvatarMappingIfUnspecified", changed);
            Set(HumanoidOversampling, state.HumanoidOversampling,
                value => state.HumanoidOversampling = value, "rig.humanoidOversampling", changed);
            Set(OptimizeGameObjects, state.OptimizeGameObjects,
                value => state.OptimizeGameObjects = value, "rig.optimizeGameObjects", changed);
            if (HasExtraExposedTransformPaths)
            {
                var value = ExtraExposedTransformPaths ?? new string[0];
                if (!ModelImporterMaterialsRigState.StringArraysEqual(state.ExtraExposedTransformPaths, value))
                    changed.Add("rig.extraExposedTransformPaths");
                state.ExtraExposedTransformPaths = (string[])value.Clone();
            }
        }

        private static void Set<T>(T? requested, T current, Action<T> setter, string path, List<string> changed)
            where T : struct
        {
            if (!requested.HasValue) return;
            setter(requested.Value);
            if (!EqualityComparer<T>.Default.Equals(requested.Value, current)) changed.Add(path);
        }
    }

    internal sealed class ModelImporterMaterialRemapState
    {
        internal string SourceName;
        internal string SourceType;
        internal UnityEngine.Object Target;

        internal string Key => SourceType + ":" + SourceName;
        internal string TargetKey => ModelImporterObjectIdentity.Key(Target);

        internal ModelImporterMaterialRemapState Clone()
        {
            return new ModelImporterMaterialRemapState
            {
                SourceName = SourceName,
                SourceType = SourceType,
                Target = Target
            };
        }
    }

    internal static class ModelImporterMaterialsRigParser
    {
        private static readonly string[] MaterialsFields = { "importMode", "location", "naming", "search" };
        private static readonly string[] RemapFields = { "source", "target" };
        private static readonly string[] SourceFields = { "type", "name" };
        private static readonly string[] ReferenceFields = { "guid", "localIdentifier" };
        private static readonly string[] RigFields =
        {
            "animationType", "avatarSetup", "sourceAvatar", "autoGenerateAvatarMappingIfUnspecified",
            "humanoidOversampling", "optimizeGameObjects", "extraExposedTransformPaths"
        };

        internal static bool TryParse(string body, ModelImporterUpdateRequest request, out string error)
        {
            return TryParseMaterials(body, request, out error) &&
                   TryParseRemaps(body, request, out error) &&
                   TryParseRig(body, request, out error);
        }

        internal static bool TryResolveReferences(ModelImporterUpdateRequest request, out string error)
        {
            if (request.MaterialRemaps != null)
            {
                for (var i = 0; i < request.MaterialRemaps.Count; i++)
                {
                    var target = request.MaterialRemaps[i].Target;
                    if (target == null) continue;
                    if (!ModelImporterObjectResolver.TryResolve(
                            target, typeof(Material), "materialRemaps[" + i + "].target", out error))
                        return false;
                }
            }

            if (request.Rig?.HasSourceAvatar == true && request.Rig.SourceAvatar != null &&
                !ModelImporterObjectResolver.TryResolve(
                    request.Rig.SourceAvatar, typeof(Avatar), "rig.sourceAvatar", out error))
                return false;

            error = null;
            return true;
        }

        private static bool TryParseMaterials(
            string body, ModelImporterUpdateRequest request, out string error)
        {
            string json;
            if (!TryGetObject(body, "materials", MaterialsFields, out json, out error)) return false;
            if (json == null) return true;

            var patch = new ModelImporterMaterialsPatch();
            if (!ReadEnum(json, "importMode", "materials.importMode", out patch.ImportMode, out error) ||
                !ReadEnum(json, "location", "materials.location", out patch.Location, out error) ||
                !ReadEnum(json, "naming", "materials.naming", out patch.Naming, out error) ||
                !ReadEnum(json, "search", "materials.search", out patch.Search, out error))
                return false;
            request.Materials = patch;
            return true;
        }

        private static bool TryParseRemaps(
            string body, ModelImporterUpdateRequest request, out string error)
        {
            List<string> elements;
            bool present;
            if (!RequestBodyReader.TryGetArrayElements(
                    body, "materialRemaps", out elements, out present, out error)) return false;
            if (!present) return true;
            if (elements.Count == 0)
            {
                error = "'materialRemaps' must contain at least one remap operation.";
                return false;
            }

            var patches = new List<ModelImporterMaterialRemapPatch>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (var i = 0; i < elements.Count; i++)
            {
                var prefix = "materialRemaps[" + i + "]";
                var element = elements[i];
                if (!RequestBodyReader.TryValidateObjectFields(element, RemapFields, out error))
                {
                    error = prefix + ": " + error;
                    return false;
                }

                var sourceJson = RequestBodyReader.GetObject(element, "source");
                if (sourceJson == null ||
                    !RequestBodyReader.TryValidateObjectFields(sourceJson, SourceFields, out error))
                {
                    error = prefix + ".source must be an object containing only 'type' and 'name'.";
                    return false;
                }
                string type;
                string name;
                bool typePresent;
                bool namePresent;
                if (!RequestBodyReader.TryGetStringValue(sourceJson, "type", out type, out typePresent) ||
                    !typePresent || type != typeof(Material).FullName)
                {
                    error = prefix + ".source.type must be 'UnityEngine.Material'.";
                    return false;
                }
                if (!RequestBodyReader.TryGetStringValue(sourceJson, "name", out name, out namePresent) ||
                    !namePresent || string.IsNullOrWhiteSpace(name))
                {
                    error = prefix + ".source.name must be a non-empty JSON string.";
                    return false;
                }
                if (!RequestBodyReader.HasTopLevelField(element, "target"))
                {
                    error = prefix + ".target is required; use null to remove a remap.";
                    return false;
                }

                ModelImporterObjectReferenceRequest target;
                if (!TryParseNullableReference(element, "target", prefix + ".target", out target, out error))
                    return false;
                var patch = new ModelImporterMaterialRemapPatch
                {
                    SourceName = name,
                    SourceType = type,
                    Target = target
                };
                if (!seen.Add(patch.Key))
                {
                    error = "Duplicate material remap source '" + name + "'.";
                    return false;
                }
                patches.Add(patch);
            }

            request.MaterialRemaps = patches;
            error = null;
            return true;
        }

        private static bool TryParseRig(string body, ModelImporterUpdateRequest request, out string error)
        {
            string json;
            if (!TryGetObject(body, "rig", RigFields, out json, out error)) return false;
            if (json == null) return true;

            var patch = new ModelImporterRigPatch();
            if (!ReadEnum(json, "animationType", "rig.animationType", out patch.AnimationType, out error) ||
                !ReadEnum(json, "avatarSetup", "rig.avatarSetup", out patch.AvatarSetup, out error) ||
                !ReadBool(json, "autoGenerateAvatarMappingIfUnspecified",
                    "rig.autoGenerateAvatarMappingIfUnspecified",
                    out patch.AutoGenerateAvatarMappingIfUnspecified, out error) ||
                !ReadEnum(json, "humanoidOversampling", "rig.humanoidOversampling",
                    out patch.HumanoidOversampling, out error) ||
                !ReadBool(json, "optimizeGameObjects", "rig.optimizeGameObjects",
                    out patch.OptimizeGameObjects, out error))
                return false;

            patch.HasSourceAvatar = RequestBodyReader.HasTopLevelField(json, "sourceAvatar");
            if (patch.HasSourceAvatar &&
                !TryParseNullableReference(json, "sourceAvatar", "rig.sourceAvatar",
                    out patch.SourceAvatar, out error)) return false;

            patch.HasExtraExposedTransformPaths =
                RequestBodyReader.HasTopLevelField(json, "extraExposedTransformPaths");
            if (patch.HasExtraExposedTransformPaths)
            {
                if (!RequestBodyReader.TryGetStringArray(
                        json, "extraExposedTransformPaths", out patch.ExtraExposedTransformPaths))
                {
                    error = "rig.extraExposedTransformPaths must be an array of strings.";
                    return false;
                }
                var seen = new HashSet<string>(StringComparer.Ordinal);
                for (var i = 0; i < patch.ExtraExposedTransformPaths.Length; i++)
                {
                    var path = patch.ExtraExposedTransformPaths[i];
                    if (string.IsNullOrWhiteSpace(path))
                    {
                        error = "rig.extraExposedTransformPaths[" + i + "] must be non-empty.";
                        return false;
                    }
                    if (!seen.Add(path))
                    {
                        error = "Duplicate extra exposed transform path '" + path + "'.";
                        return false;
                    }
                }
            }

            request.Rig = patch;
            error = null;
            return true;
        }

        private static bool TryGetObject(
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

        private static bool TryParseNullableReference(
            string json, string key, string path, out ModelImporterObjectReferenceRequest reference,
            out string error)
        {
            reference = null;
            var raw = RequestBodyReader.GetRawValue(json, key);
            if (raw == null)
            {
                error = path + " must be null or a JSON object.";
                return false;
            }
            if (raw.Trim() == "null")
            {
                error = null;
                return true;
            }

            var objectJson = RequestBodyReader.GetObject(json, key);
            if (objectJson == null ||
                !RequestBodyReader.TryValidateObjectFields(objectJson, ReferenceFields, out error))
            {
                error = path + " must contain only 'guid' and optional 'localIdentifier'.";
                return false;
            }
            string guid;
            string localIdentifier;
            bool guidPresent;
            bool localPresent;
            if (!RequestBodyReader.TryGetStringValue(objectJson, "guid", out guid, out guidPresent) ||
                !guidPresent || string.IsNullOrWhiteSpace(guid))
            {
                error = path + ".guid must be a non-empty JSON string.";
                return false;
            }
            if (!RequestBodyReader.TryGetStringValue(
                    objectJson, "localIdentifier", out localIdentifier, out localPresent))
            {
                error = path + ".localIdentifier must be a decimal JSON string.";
                return false;
            }
            ulong ignored;
            if (localPresent && (!ulong.TryParse(localIdentifier, NumberStyles.None,
                    CultureInfo.InvariantCulture, out ignored)))
            {
                error = path + ".localIdentifier must be an unsigned decimal JSON string.";
                return false;
            }
            reference = new ModelImporterObjectReferenceRequest
            {
                Guid = guid,
                LocalIdentifier = localPresent ? localIdentifier : null
            };
            error = null;
            return true;
        }

        private static bool ReadBool(
            string json, string key, string path, out bool? value, out string error)
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

        private static bool ReadEnum<T>(
            string json, string key, string path, out T? value, out string error) where T : struct
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
    }

    internal static class ModelImporterObjectResolver
    {
        internal static bool TryResolve(
            ModelImporterObjectReferenceRequest reference,
            Type expectedType,
            string path,
            out string error)
        {
            var assetPath = AssetDatabase.GUIDToAssetPath(reference.Guid);
            if (string.IsNullOrEmpty(assetPath))
            {
                error = path + " refers to an unknown asset GUID.";
                return false;
            }

            var all = AssetDatabase.LoadAllAssetsAtPath(assetPath);
            if (!string.IsNullOrEmpty(reference.LocalIdentifier))
            {
                foreach (var asset in all)
                {
                    if (asset == null || ModelImporterObjectIdentity.LocalIdentifier(asset) != reference.LocalIdentifier)
                        continue;
                    if (!expectedType.IsInstanceOfType(asset))
                    {
                        error = path + " resolves to " + asset.GetType().FullName +
                                ", expected " + expectedType.FullName + ".";
                        return false;
                    }
                    reference.Resolved = asset;
                    error = null;
                    return true;
                }
                error = path + " localIdentifier was not found in the referenced asset.";
                return false;
            }

            var candidates = new List<UnityEngine.Object>();
            foreach (var asset in all)
                if (asset != null && expectedType.IsInstanceOfType(asset)) candidates.Add(asset);
            if (candidates.Count == 0)
            {
                error = path + " contains no " + expectedType.FullName + ".";
                return false;
            }
            if (candidates.Count > 1)
            {
                error = path + " is ambiguous; provide localIdentifier for one of the " +
                        candidates.Count + " matching objects.";
                return false;
            }

            reference.Resolved = candidates[0];
            reference.LocalIdentifier = ModelImporterObjectIdentity.LocalIdentifier(candidates[0]);
            error = null;
            return true;
        }
    }

    internal static class ModelImporterObjectIdentity
    {
        internal static string LocalIdentifier(UnityEngine.Object asset)
            => GlobalObjectId.GetGlobalObjectIdSlow(asset).targetObjectId.ToString(CultureInfo.InvariantCulture);

        internal static string Key(UnityEngine.Object asset)
        {
            if (asset == null) return string.Empty;
            var path = AssetDatabase.GetAssetPath(asset);
            return AssetDatabase.AssetPathToGUID(path) + ":" + LocalIdentifier(asset);
        }

        internal static bool Same(UnityEngine.Object left, UnityEngine.Object right)
            => Key(left) == Key(right);
    }

    internal static class ModelImporterMaterialsRigState
    {
        internal static void Capture(ModelImporter importer, ModelImporterState state)
        {
            state.MaterialImportMode = importer.materialImportMode;
            state.MaterialLocation = importer.materialLocation;
            state.MaterialName = importer.materialName;
            state.MaterialSearch = importer.materialSearch;
            state.MaterialRemaps = new List<ModelImporterMaterialRemapState>();
            foreach (var entry in importer.GetExternalObjectMap())
            {
                if (entry.Key.type != typeof(Material)) continue;
                state.MaterialRemaps.Add(new ModelImporterMaterialRemapState
                {
                    SourceName = entry.Key.name,
                    SourceType = entry.Key.type.FullName,
                    Target = entry.Value
                });
            }
            state.MaterialRemaps.Sort((a, b) => string.Compare(a.Key, b.Key, StringComparison.Ordinal));

            state.AnimationType = importer.animationType;
            state.AvatarSetup = importer.avatarSetup;
            state.SourceAvatar = importer.sourceAvatar;
            state.AutoGenerateAvatarMappingIfUnspecified = importer.autoGenerateAvatarMappingIfUnspecified;
            state.HumanoidOversampling = importer.humanoidOversampling;
            state.OptimizeGameObjects = importer.optimizeGameObjects;
            state.ExtraExposedTransformPaths = importer.extraExposedTransformPaths ?? new string[0];
        }

        internal static void CloneCollections(ModelImporterState source, ModelImporterState clone)
        {
            clone.MaterialRemaps = new List<ModelImporterMaterialRemapState>();
            if (source.MaterialRemaps != null)
                foreach (var remap in source.MaterialRemaps) clone.MaterialRemaps.Add(remap.Clone());
            clone.ExtraExposedTransformPaths =
                source.ExtraExposedTransformPaths == null
                    ? new string[0]
                    : (string[])source.ExtraExposedTransformPaths.Clone();
        }

        internal static void Apply(
            ModelImporter importer, ModelImporterState state, ModelImporterUpdateRequest request)
        {
            if (request == null) return;
            if (request.Materials?.ImportMode.HasValue == true)
                importer.materialImportMode = state.MaterialImportMode;
            if (request.Materials?.Location.HasValue == true)
                importer.materialLocation = state.MaterialLocation;
            if (request.Materials?.Naming.HasValue == true)
                importer.materialName = state.MaterialName;
            if (request.Materials?.Search.HasValue == true)
                importer.materialSearch = state.MaterialSearch;

            if (request.Rig?.AnimationType.HasValue == true)
                importer.animationType = state.AnimationType;
            if (request.Rig?.AvatarSetup.HasValue == true)
                importer.avatarSetup = state.AvatarSetup;
            if (request.Rig?.HasSourceAvatar == true)
                importer.sourceAvatar = state.SourceAvatar;
            if (request.Rig?.AutoGenerateAvatarMappingIfUnspecified.HasValue == true)
                importer.autoGenerateAvatarMappingIfUnspecified = state.AutoGenerateAvatarMappingIfUnspecified;
            if (request.Rig?.HumanoidOversampling.HasValue == true)
                importer.humanoidOversampling = state.HumanoidOversampling;
            if (request.Rig?.OptimizeGameObjects.HasValue == true)
                importer.optimizeGameObjects = state.OptimizeGameObjects;
            if (request.Rig?.HasExtraExposedTransformPaths == true)
                importer.extraExposedTransformPaths = state.ExtraExposedTransformPaths ?? new string[0];
            if (request.MaterialRemaps != null) ReplaceRemaps(importer, state.MaterialRemaps);
        }

        internal static void ApplyRemapPatches(
            ModelImporterState state,
            List<ModelImporterMaterialRemapPatch> patches,
            List<string> changed)
        {
            foreach (var patch in patches)
            {
                var index = state.MaterialRemaps.FindIndex(item => item.Key == patch.Key);
                if (patch.Target == null)
                {
                    if (index < 0) continue;
                    state.MaterialRemaps.RemoveAt(index);
                    changed.Add("materialRemaps[" + patch.SourceName + "]");
                    continue;
                }

                var target = patch.Target.Resolved;
                if (index >= 0 && ModelImporterObjectIdentity.Same(state.MaterialRemaps[index].Target, target))
                    continue;
                var value = new ModelImporterMaterialRemapState
                {
                    SourceName = patch.SourceName,
                    SourceType = patch.SourceType,
                    Target = target
                };
                if (index >= 0) state.MaterialRemaps[index] = value;
                else state.MaterialRemaps.Add(value);
                changed.Add("materialRemaps[" + patch.SourceName + "]");
            }
            state.MaterialRemaps.Sort((a, b) => string.Compare(a.Key, b.Key, StringComparison.Ordinal));
        }

        internal static bool EqualsState(ModelImporterState left, ModelImporterState right)
        {
            return left.MaterialImportMode == right.MaterialImportMode &&
                   left.MaterialLocation == right.MaterialLocation &&
                   left.MaterialName == right.MaterialName &&
                   left.MaterialSearch == right.MaterialSearch &&
                   RemapsEqual(left.MaterialRemaps, right.MaterialRemaps) &&
                   left.AnimationType == right.AnimationType &&
                   left.AvatarSetup == right.AvatarSetup &&
                   ModelImporterObjectIdentity.Same(left.SourceAvatar, right.SourceAvatar) &&
                   left.AutoGenerateAvatarMappingIfUnspecified == right.AutoGenerateAvatarMappingIfUnspecified &&
                   left.HumanoidOversampling == right.HumanoidOversampling &&
                   left.OptimizeGameObjects == right.OptimizeGameObjects &&
                   StringArraysEqual(left.ExtraExposedTransformPaths, right.ExtraExposedTransformPaths);
        }

        internal static bool StringArraysEqual(string[] left, string[] right)
        {
            left = left ?? new string[0];
            right = right ?? new string[0];
            if (left.Length != right.Length) return false;
            for (var i = 0; i < left.Length; i++)
                if (left[i] != right[i]) return false;
            return true;
        }

        private static bool RemapsEqual(
            List<ModelImporterMaterialRemapState> left,
            List<ModelImporterMaterialRemapState> right)
        {
            left = left ?? new List<ModelImporterMaterialRemapState>();
            right = right ?? new List<ModelImporterMaterialRemapState>();
            if (left.Count != right.Count) return false;
            for (var i = 0; i < left.Count; i++)
                if (left[i].Key != right[i].Key || left[i].TargetKey != right[i].TargetKey) return false;
            return true;
        }

        private static void ReplaceRemaps(
            ModelImporter importer, List<ModelImporterMaterialRemapState> desired)
        {
            var current = importer.GetExternalObjectMap();
            foreach (var entry in current)
                if (entry.Key.type == typeof(Material)) importer.RemoveRemap(entry.Key);
            if (desired == null) return;
            foreach (var remap in desired)
                importer.AddRemap(
                    new AssetImporter.SourceAssetIdentifier(typeof(Material), remap.SourceName),
                    remap.Target);
        }
    }

    internal static class ModelImporterMaterialsRigRules
    {
        internal static bool TryValidate(
            ModelImporterState state,
            ModelImporter importer,
            ModelImporterUpdateRequest request,
            out string error)
        {
            if (request.Materials != null && state.MaterialImportMode == ModelImporterMaterialImportMode.None &&
                (request.Materials.Location.HasValue || request.Materials.Naming.HasValue ||
                 request.Materials.Search.HasValue))
            {
                error = "Material location, naming, and search are incompatible with materials.importMode None.";
                return false;
            }
            if (request.Materials != null && state.MaterialLocation == ModelImporterMaterialLocation.InPrefab &&
                (request.Materials.Naming.HasValue || request.Materials.Search.HasValue))
            {
                error = "Material naming and search are incompatible with materials.location InPrefab.";
                return false;
            }
            if (request.MaterialRemaps != null &&
                state.MaterialImportMode == ModelImporterMaterialImportMode.None &&
                request.MaterialRemaps.Exists(remap => remap.Target != null))
            {
                error = "Adding or replacing materialRemaps is incompatible with materials.importMode None.";
                return false;
            }

            var rig = request.Rig;
            if (rig == null)
            {
                error = null;
                return true;
            }
            var avatarFieldsTouched = rig.AnimationType.HasValue || rig.AvatarSetup.HasValue || rig.HasSourceAvatar;
            if (avatarFieldsTouched &&
                (state.AnimationType == ModelImporterAnimationType.None ||
                 state.AnimationType == ModelImporterAnimationType.Legacy) &&
                (state.AvatarSetup != ModelImporterAvatarSetup.NoAvatar || state.SourceAvatar != null))
            {
                error = "rig.animationType None or Legacy requires avatarSetup NoAvatar and sourceAvatar null.";
                return false;
            }
            if (avatarFieldsTouched && state.AvatarSetup == ModelImporterAvatarSetup.CopyFromOther)
            {
                if (state.SourceAvatar == null)
                {
                    error = "rig.avatarSetup CopyFromOther requires rig.sourceAvatar.";
                    return false;
                }
                if (!state.SourceAvatar.isValid)
                {
                    error = "rig.sourceAvatar must be a valid Avatar.";
                    return false;
                }
                if (state.AnimationType == ModelImporterAnimationType.Human && !state.SourceAvatar.isHuman)
                {
                    error = "A Human rig requires a humanoid source Avatar.";
                    return false;
                }
                if (state.AnimationType == ModelImporterAnimationType.Generic && state.SourceAvatar.isHuman)
                {
                    error = "A Generic rig requires a non-humanoid source Avatar.";
                    return false;
                }
            }
            else if (avatarFieldsTouched && state.SourceAvatar != null)
            {
                error = "rig.sourceAvatar is allowed only with avatarSetup CopyFromOther.";
                return false;
            }
            if (rig.AutoGenerateAvatarMappingIfUnspecified == true &&
                (state.AnimationType != ModelImporterAnimationType.Human ||
                 state.AvatarSetup != ModelImporterAvatarSetup.CreateFromThisModel))
            {
                error = "rig.autoGenerateAvatarMappingIfUnspecified requires a Human CreateFromThisModel rig.";
                return false;
            }
            if (rig.HumanoidOversampling.HasValue &&
                state.AnimationType != ModelImporterAnimationType.Human)
            {
                error = "rig.humanoidOversampling is supported only for a Human rig.";
                return false;
            }
            if ((rig.HasExtraExposedTransformPaths || rig.OptimizeGameObjects.HasValue) &&
                !state.OptimizeGameObjects && state.ExtraExposedTransformPaths.Length > 0)
            {
                error = "rig.extraExposedTransformPaths must be empty when optimizeGameObjects is false.";
                return false;
            }

            error = null;
            return true;
        }
    }
}
