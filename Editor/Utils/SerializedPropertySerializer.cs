using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace LeonAkasaka.UnionAir.Editor
{
    /// <summary>
    /// Shared utilities for reading and writing SerializedProperty values as JSON.
    /// Extracted from ComponentWriteHandler so that ScriptableObject handlers can reuse the same logic.
    /// </summary>
    internal static class SerializedPropertySerializer
    {
        private static readonly string[] AssetObjectReferenceFields =
        {
            "assetGuid", "assetPath", "assetType", "localIdentifier"
        };

        /// <summary>
        /// Applies the value of <paramref name="jsonKey"/> to <paramref name="prop"/>.
        /// </summary>
        /// <remarks>
        /// The two write paths differ in one place only: the component endpoint resolves scene
        /// objects for an <c>ObjectReference</c> and the ScriptableObject endpoint does not. Array
        /// elements go through the same difference, so the array writer takes its caller's function
        /// rather than picking one.
        /// </remarks>
        internal delegate bool ApplyProperty(
            SerializedProperty prop, string json, string jsonKey, out string error, out int statusCode);

        /// <summary>What an array-addressing key in <c>properties</c> names.</summary>
        internal enum ArrayAddress
        {
            /// <summary>Not an array address.</summary>
            None,
            /// <summary>One element: <c>m_Materials.Array.data[0]</c>.</summary>
            Element,
            /// <summary>The length: <c>m_Materials.Array.size</c>.</summary>
            Size
        }

        private const string ArraySegment    = ".Array.";
        private const string ArraySizeSuffix = ".Array.size";
        private const string ArrayDataMarker = ".Array.data[";

        /// <summary>
        /// The longest array a request may ask for.
        /// </summary>
        /// <remarks>
        /// Not a claim about what Unity supports, and not a length any real project reaches. It is
        /// here because <c>Array.size</c> is the one write whose cost is unbounded by the request
        /// that carries it: a whole-array write pays for every element in the body, while a
        /// forty-byte request can name two billion. Unity allocates what it is told, so without a
        /// bound a mistyped length takes the Editor down and any unsaved scene with it.
        /// </remarks>
        internal const int MaxArrayLength = 1000000;

        // ── Read direction ────────────────────────────────────────────────────

        /// <summary>
        /// Appends the JSON representation of <paramref name="prop"/> to <paramref name="sb"/>.
        /// ObjectReferences to scene objects (non-asset) are emitted as <c>null</c>.
        /// </summary>
        public static void SerializePropertyToJson(SerializedProperty prop, StringBuilder sb)
            => SerializePropertyToJson(prop, sb, false);

        /// <summary>
        /// Appends the JSON representation of <paramref name="prop"/> to <paramref name="sb"/>.
        /// </summary>
        /// <param name="sceneObjectsResolvable">
        /// Whether the endpoint reading this can resolve a scene object reference. A component
        /// read can and reports one; an asset read cannot -- an asset cannot hold a scene
        /// reference and the ScriptableObject write accepts only the asset fields -- so it emits
        /// <c>null</c> rather than a spelling that endpoint would refuse.
        /// </param>
        public static void SerializePropertyToJson(
            SerializedProperty prop, StringBuilder sb, bool sceneObjectsResolvable)
        {
            // Serialize arrays (Unity internally represents strings as char arrays, so exclude those)
            if (prop.isArray && prop.propertyType != SerializedPropertyType.String)
            {
                sb.Append('[');
                for (int i = 0; i < prop.arraySize; i++)
                {
                    if (i > 0) sb.Append(',');
                    SerializePropertyToJson(prop.GetArrayElementAtIndex(i), sb, sceneObjectsResolvable);
                }
                sb.Append(']');
                return;
            }

            switch (prop.propertyType)
            {
                case SerializedPropertyType.Boolean:
                    sb.Append(prop.boolValue ? "true" : "false");
                    break;

                case SerializedPropertyType.Integer:
                case SerializedPropertyType.LayerMask:
                case SerializedPropertyType.Enum:
                    sb.Append(prop.intValue);
                    break;

                case SerializedPropertyType.Float:
                    sb.Append(RestResponse.FormatFloat(prop.floatValue));
                    break;

                case SerializedPropertyType.String:
                    sb.Append('"');
                    sb.Append(RestResponse.EscapeJson(prop.stringValue));
                    sb.Append('"');
                    break;

                case SerializedPropertyType.Color:
                {
                    var c = prop.colorValue;
                    sb.Append($"{{\"r\":{RestResponse.FormatFloat(c.r)},\"g\":{RestResponse.FormatFloat(c.g)},\"b\":{RestResponse.FormatFloat(c.b)},\"a\":{RestResponse.FormatFloat(c.a)}}}");
                    break;
                }
                case SerializedPropertyType.Vector2:
                {
                    var v = prop.vector2Value;
                    sb.Append($"{{\"x\":{RestResponse.FormatFloat(v.x)},\"y\":{RestResponse.FormatFloat(v.y)}}}");
                    break;
                }
                case SerializedPropertyType.Vector3:
                {
                    var v = prop.vector3Value;
                    sb.Append($"{{\"x\":{RestResponse.FormatFloat(v.x)},\"y\":{RestResponse.FormatFloat(v.y)},\"z\":{RestResponse.FormatFloat(v.z)}}}");
                    break;
                }
                case SerializedPropertyType.Vector4:
                {
                    var v = prop.vector4Value;
                    sb.Append($"{{\"x\":{RestResponse.FormatFloat(v.x)},\"y\":{RestResponse.FormatFloat(v.y)},\"z\":{RestResponse.FormatFloat(v.z)},\"w\":{RestResponse.FormatFloat(v.w)}}}");
                    break;
                }
                case SerializedPropertyType.Quaternion:
                {
                    var q = prop.quaternionValue;
                    sb.Append($"{{\"x\":{RestResponse.FormatFloat(q.x)},\"y\":{RestResponse.FormatFloat(q.y)},\"z\":{RestResponse.FormatFloat(q.z)},\"w\":{RestResponse.FormatFloat(q.w)}}}");
                    break;
                }
                case SerializedPropertyType.Rect:
                {
                    var r = prop.rectValue;
                    sb.Append($"{{\"x\":{RestResponse.FormatFloat(r.x)},\"y\":{RestResponse.FormatFloat(r.y)},\"width\":{RestResponse.FormatFloat(r.width)},\"height\":{RestResponse.FormatFloat(r.height)}}}");
                    break;
                }
                case SerializedPropertyType.Bounds:
                {
                    var b = prop.boundsValue;
                    sb.Append($"{{\"center\":{{\"x\":{RestResponse.FormatFloat(b.center.x)},\"y\":{RestResponse.FormatFloat(b.center.y)},\"z\":{RestResponse.FormatFloat(b.center.z)}}},");
                    sb.Append($"\"extents\":{{\"x\":{RestResponse.FormatFloat(b.extents.x)},\"y\":{RestResponse.FormatFloat(b.extents.y)},\"z\":{RestResponse.FormatFloat(b.extents.z)}}}}}");
                    break;
                }
                case SerializedPropertyType.ObjectReference:
                    AppendObjectReferenceJson(sb, prop.objectReferenceValue, sceneObjectsResolvable);
                    break;
                default:
                    // Generic (arrays, nested structs) and unsupported types → null
                    sb.Append("null");
                    break;
            }
        }

        /// <summary>
        /// Appends an <c>ObjectReference</c> value in the vocabulary the write endpoints
        /// accept, so a value read out of a component can be sent straight back.
        ///
        /// The read used to describe what the object is -- a class name, a display name, and
        /// an identity -- while the write describes how to find one, and the two never agreed.
        /// <c>type</c> was the sharp edge: it carried the object's class in the read and the
        /// kind of reference in the write, so a client echoing a read was told its type was
        /// unknown, about a field it never meant to fill in. Here <c>type</c> has the write's
        /// meaning and nothing else.
        ///
        /// <paramref name="sceneObjectsResolvable"/> says whether the caller's own write
        /// accepts a scene reference. Each read emits what its write takes: the component
        /// endpoint resolves <c>globalObjectId</c>, the ScriptableObject endpoint does not, and
        /// reporting a reference the matching write would refuse is the defect this replaces
        /// rather than a smaller version of it. An asset is spelled identically either way,
        /// which is what makes the two reads agree.
        ///
        /// The display name is gone. It was the readable half, and no field of the write
        /// carries it -- keeping it would mean the write either refusing the read again or
        /// accepting a key it ignores, which is the silence #104 removed. <c>assetPath</c>
        /// names an asset; a scene object is named by resolving it.
        /// </summary>
        internal static void AppendObjectReferenceJson(
            StringBuilder sb, UnityEngine.Object obj, bool sceneObjectsResolvable)
        {
            if (obj == null)
            {
                sb.Append("null");
                return;
            }

            var assetPath = AssetDatabase.GetAssetPath(obj);
            if (!string.IsNullOrEmpty(assetPath))
            {
                var assetGuid = AssetDatabase.AssetPathToGUID(assetPath);
                sb.Append($"{{\"assetGuid\":\"{RestResponse.EscapeJson(assetGuid)}\",");
                sb.Append($"\"assetPath\":\"{RestResponse.EscapeJson(assetPath)}\",");
                sb.Append($"\"assetType\":\"{RestResponse.EscapeJson(obj.GetType().FullName)}\"");

                // Reported for every reference and not only for a sub-asset, because deciding
                // per target would make the response shape depend on what it points at. A file
                // holding one object reports it too and is no worse for it; a file holding
                // twenty-three meshes cannot be addressed without it.
                //
                // Decimal string so 64 bits survive JSON, which is the spelling
                // GET /api/assets/model-importer/{guid} already uses for the same concept.
                if (AssetDatabase.TryGetGUIDAndLocalFileIdentifier(obj, out _, out long localId))
                    sb.Append($",\"localIdentifier\":\"{localId.ToString(System.Globalization.CultureInfo.InvariantCulture)}\"");

                sb.Append("}");
                return;
            }

            if (!sceneObjectsResolvable)
            {
                sb.Append("null");
                return;
            }

            sb.Append("{\"type\":\"globalObjectId\",\"value\":");
            sb.Append($"\"{RestResponse.EscapeJson(ObjectIdUtils.GetGlobalObjectId(obj))}\"}}");
        }

        // ── Write direction ───────────────────────────────────────────────────

        /// <summary>
        /// Tries to apply the value for <paramref name="jsonKey"/> from <paramref name="json"/>
        /// to <paramref name="prop"/>.  Returns <c>true</c> if the property was updated.
        /// </summary>
        /// <remarks>
        /// Every <c>false</c> carries an <paramref name="error"/> saying why. A caller sends a key
        /// only after matching it to this property, so a value that cannot be applied is a request
        /// to reject and not a property to pass over: the alternative is answering <c>200</c> for a
        /// write that did not happen, which is what a client cannot see.
        /// </remarks>
        public static bool ApplyPropertyFromJson(
            SerializedProperty prop, string json, string jsonKey, out string error, out int statusCode)
        {
            error = null;
            statusCode = 400;

            // An array reaches its own writer, chosen by the key rather than by the property, so
            // one arriving here is a caller that skipped that dispatch rather than a request to
            // refuse. Saying which beats falling through to "serialized type Generic".
            if (IsWritableAsArray(prop))
            {
                error = $"Property {jsonKey} is an array and is not written one value at a time.";
                return false;
            }

            try
            {
                switch (prop.propertyType)
                {
                    case SerializedPropertyType.Boolean:
                    {
                        if (RequestBodyReader.TryGetBoolValue(
                                json, jsonKey, out var value, out var present) && present)
                        {
                            prop.boolValue = value;
                            return true;
                        }
                        error = Expected(jsonKey, "a JSON boolean");
                        break;
                    }
                    case SerializedPropertyType.Integer:
                    case SerializedPropertyType.LayerMask:
                    case SerializedPropertyType.Enum:
                    {
                        if (RequestBodyReader.TryGetIntValue(
                                json, jsonKey, out var value, out var present) && present)
                        {
                            prop.intValue = value;
                            return true;
                        }
                        error = Expected(jsonKey, "a JSON integer");
                        break;
                    }
                    case SerializedPropertyType.Float:
                    {
                        if (RequestBodyReader.TryGetFloatValue(
                                json, jsonKey, out var value, out var present) && present)
                        {
                            prop.floatValue = value;
                            return true;
                        }
                        error = Expected(jsonKey, "a JSON number");
                        break;
                    }
                    case SerializedPropertyType.String:
                    {
                        if (RequestBodyReader.TryGetStringValue(
                                json, jsonKey, out var value, out var present) && present)
                        {
                            prop.stringValue = value;
                            return true;
                        }
                        error = Expected(jsonKey, "a JSON string");
                        break;
                    }
                    case SerializedPropertyType.Color:
                    {
                        var obj = RequestBodyReader.GetObject(json, jsonKey);
                        if (obj != null && TryValidateCompositeObject(
                                obj, jsonKey, new[] { "r", "g", "b", "a" }, out error))
                        {
                            bool hasR, hasG, hasB, hasA;
                            float r, g, b, a;
                            if (!TryReadFloatMember(obj, jsonKey, "r", prop.colorValue.r, out r, out hasR, out error) ||
                                !TryReadFloatMember(obj, jsonKey, "g", prop.colorValue.g, out g, out hasG, out error) ||
                                !TryReadFloatMember(obj, jsonKey, "b", prop.colorValue.b, out b, out hasB, out error) ||
                                !TryReadFloatMember(obj, jsonKey, "a", prop.colorValue.a, out a, out hasA, out error))
                                break;
                            if (!hasR && !hasG && !hasB && !hasA)
                            {
                                error = Expected(jsonKey, "a JSON object containing at least one of r, g, b, or a");
                                break;
                            }
                            prop.colorValue = new Color(r, g, b, a);
                            return true;
                        }
                        if (error == null)
                            error = Expected(jsonKey, "a JSON object with r, g, b, and a members");
                        break;
                    }
                    case SerializedPropertyType.Vector2:
                    {
                        var obj = RequestBodyReader.GetObject(json, jsonKey);
                        if (obj != null && TryValidateCompositeObject(
                                obj, jsonKey, new[] { "x", "y" }, out error))
                        {
                            bool hasX, hasY;
                            float x, y;
                            if (!TryReadFloatMember(obj, jsonKey, "x", prop.vector2Value.x, out x, out hasX, out error) ||
                                !TryReadFloatMember(obj, jsonKey, "y", prop.vector2Value.y, out y, out hasY, out error))
                                break;
                            if (!hasX && !hasY)
                            {
                                error = Expected(jsonKey, "a JSON object containing at least one of x or y");
                                break;
                            }
                            prop.vector2Value = new Vector2(x, y);
                            return true;
                        }
                        if (error == null)
                            error = Expected(jsonKey, "a JSON object with x and y members");
                        break;
                    }
                    case SerializedPropertyType.Vector3:
                    {
                        var obj = RequestBodyReader.GetObject(json, jsonKey);
                        if (obj != null && TryValidateCompositeObject(
                                obj, jsonKey, new[] { "x", "y", "z" }, out error))
                        {
                            bool hasX, hasY, hasZ;
                            float x, y, z;
                            if (!TryReadFloatMember(obj, jsonKey, "x", prop.vector3Value.x, out x, out hasX, out error) ||
                                !TryReadFloatMember(obj, jsonKey, "y", prop.vector3Value.y, out y, out hasY, out error) ||
                                !TryReadFloatMember(obj, jsonKey, "z", prop.vector3Value.z, out z, out hasZ, out error))
                                break;
                            if (!hasX && !hasY && !hasZ)
                            {
                                error = Expected(jsonKey, "a JSON object containing at least one of x, y, or z");
                                break;
                            }
                            prop.vector3Value = new Vector3(x, y, z);
                            return true;
                        }
                        if (error == null)
                            error = Expected(jsonKey, "a JSON object with x, y, and z members");
                        break;
                    }
                    case SerializedPropertyType.Vector4:
                    {
                        var obj = RequestBodyReader.GetObject(json, jsonKey);
                        if (obj != null && TryValidateCompositeObject(
                                obj, jsonKey, new[] { "x", "y", "z", "w" }, out error))
                        {
                            bool hasX, hasY, hasZ, hasW;
                            float x, y, z, w;
                            if (!TryReadFloatMember(obj, jsonKey, "x", prop.vector4Value.x, out x, out hasX, out error) ||
                                !TryReadFloatMember(obj, jsonKey, "y", prop.vector4Value.y, out y, out hasY, out error) ||
                                !TryReadFloatMember(obj, jsonKey, "z", prop.vector4Value.z, out z, out hasZ, out error) ||
                                !TryReadFloatMember(obj, jsonKey, "w", prop.vector4Value.w, out w, out hasW, out error))
                                break;
                            if (!hasX && !hasY && !hasZ && !hasW)
                            {
                                error = Expected(jsonKey, "a JSON object containing at least one of x, y, z, or w");
                                break;
                            }
                            prop.vector4Value = new Vector4(x, y, z, w);
                            return true;
                        }
                        if (error == null)
                            error = Expected(jsonKey, "a JSON object with x, y, z, and w members");
                        break;
                    }
                    case SerializedPropertyType.ObjectReference:
                    {
                        UnityEngine.Object value;
                        if (TryResolveObjectReference(json, jsonKey, prop, out value, out error, out statusCode))
                        {
                            prop.objectReferenceValue = value;
                            return true;
                        }
                        if (error == null)
                            error = Expected(jsonKey, "null or a JSON object naming an object or an asset");
                        break;
                    }
                    default:
                    {
                        // Read-only by omission rather than by decision: Quaternion, Rect and Bounds
                        // are serialized by the read direction and have no write case here. Saying
                        // so beats reporting the write as done.
                        error = $"Property {jsonKey} has serialized type {prop.propertyType}, " +
                                "which this endpoint cannot write.";
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                error = $"Failed to update property {jsonKey}: {ex.Message}";
                statusCode = 400;
            }
            return false;
        }

        private static string Expected(string jsonKey, string shape)
            => $"Property {jsonKey} expects {shape}.";

        /// <summary>
        /// Whether <paramref name="prop"/> is an array this endpoint addresses as one.
        /// </summary>
        /// <remarks>
        /// Unity represents a string as a char array internally, so <c>isArray</c> is true for a
        /// string field. Those are written by the String case and are not arrays to any caller.
        /// </remarks>
        internal static bool IsWritableAsArray(SerializedProperty prop)
            => prop.isArray && prop.propertyType != SerializedPropertyType.String;

        /// <summary>
        /// Whether <paramref name="key"/> reaches into an array in any form, addressable or not.
        /// </summary>
        internal static bool NamesArrayInternals(string key)
            => !string.IsNullOrEmpty(key) && key.IndexOf(ArraySegment, StringComparison.Ordinal) >= 0;

        /// <summary>
        /// Parses <paramref name="key"/> as one of the two array addresses this endpoint accepts,
        /// reporting the array it reaches into and, for an element, which one.
        /// </summary>
        /// <remarks>
        /// Both spellings are Unity's own: they are what <c>SerializedProperty.propertyPath</c>
        /// returns for an element and for the length. Exactly these two are accepted, so a key
        /// reaching past an element -- <c>m_Items.Array.data[0].hp</c> -- fails to parse and is
        /// refused by the caller rather than being resolved by whichever walk happens to reach it.
        /// A nested array beneath an element fails the same way, because its elements are Generic
        /// and unwritable regardless.
        /// </remarks>
        internal static bool TryParseArrayAddress(
            string key, out string arrayPath, out ArrayAddress address, out int index)
        {
            arrayPath = null;
            address = ArrayAddress.None;
            index = -1;
            if (string.IsNullOrEmpty(key)) return false;

            if (key.EndsWith(ArraySizeSuffix, StringComparison.Ordinal))
            {
                var prefix = key.Substring(0, key.Length - ArraySizeSuffix.Length);
                if (prefix.Length == 0 || NamesArrayInternals(prefix)) return false;
                arrayPath = prefix;
                address = ArrayAddress.Size;
                return true;
            }

            if (key[key.Length - 1] != ']') return false;

            var marker = key.IndexOf(ArrayDataMarker, StringComparison.Ordinal);
            if (marker <= 0) return false;

            var digitsStart = marker + ArrayDataMarker.Length;
            var digitsLength = key.Length - 1 - digitsStart;
            if (digitsLength <= 0) return false;
            for (int i = digitsStart; i < key.Length - 1; i++)
                if (key[i] < '0' || key[i] > '9') return false;
            if (!int.TryParse(key.Substring(digitsStart, digitsLength), out index)) return false;

            arrayPath = key.Substring(0, marker);
            address = ArrayAddress.Element;
            return true;
        }

        /// <summary>
        /// Returns why the elements of <paramref name="arrayProp"/> cannot be written, or null when
        /// they can.
        /// </summary>
        /// <remarks>
        /// An element the read direction serializes as <c>null</c> is one a caller has never seen,
        /// so a write that replaces or drops it destroys content on the caller's behalf. That rules
        /// out a whole-array write and a shrinking resize; growing alone would be safe, but a
        /// contract permitting one direction of a resize and not the other is worse to describe
        /// than one that refuses the array.
        ///
        /// An empty array has an element type all the same, and answering "writable" because there
        /// is nothing to look at makes writability depend on the array's current length. A caller
        /// clearing an empty list would be told <c>200</c> and the same request against a list with
        /// one element <c>400</c> -- the state-dependent success this endpoint exists to not give.
        /// So an empty array is grown by one to be read and put back, which no caller can observe:
        /// nothing here is committed until <c>ApplyModifiedProperties</c>, and the length is
        /// restored before this returns.
        /// </remarks>
        internal static string DescribeUnwritableElements(SerializedProperty arrayProp, string jsonKey)
        {
            var probing = arrayProp.arraySize == 0;
            if (probing) arrayProp.arraySize = 1;

            var element = arrayProp.GetArrayElementAtIndex(0);
            var isGeneric = element.propertyType == SerializedPropertyType.Generic;
            var elementType = element.type;

            if (probing) arrayProp.arraySize = 0;

            if (!isGeneric) return null;

            return $"Property {jsonKey} is an array of {elementType} elements, whose serialized " +
                   "type this endpoint cannot write. Only arrays of a type it writes can be " +
                   "addressed.";
        }

        /// <summary>
        /// Replaces every element of <paramref name="arrayProp"/> with the JSON array at
        /// <paramref name="jsonKey"/>, resizing it to that array's length.
        /// </summary>
        /// <remarks>
        /// A replacement rather than a merge, because Unity keeps no identity per element and
        /// rewrites the array wholesale; the same reasoning the AnimationClip event array follows.
        ///
        /// Each element is handed to <paramref name="applyElement"/> as a one-field object keyed by
        /// the element's own address. Every typed reader selects a value by key from an object, so
        /// this reuses them exactly as a top-level property would, and an element that cannot be
        /// applied is named by the address a caller can address it with rather than by the array's.
        /// </remarks>
        internal static bool TryWriteArray(
            SerializedProperty arrayProp,
            string json,
            string jsonKey,
            ApplyProperty applyElement,
            out string error,
            out int statusCode)
        {
            statusCode = 400;

            var raw = RequestBodyReader.GetRawValue(json, jsonKey);
            if (raw == null)
            {
                error = $"Property {jsonKey} is not a well-formed JSON value.";
                return false;
            }
            raw = raw.Trim();
            if (raw.Length == 0 || raw[0] != '[')
            {
                error = Expected(jsonKey, "a JSON array");
                return false;
            }

            if (!RequestBodyReader.TryGetArrayElements(
                    json, jsonKey, out var elements, out _, out var arrayError))
            {
                error = $"Property {jsonKey} is not a well-formed JSON array: {arrayError}";
                return false;
            }

            if (!TryBoundLength(elements.Count, jsonKey, out error)) return false;

            // Asked of the array rather than of what it currently holds, so an empty one is judged
            // by its element type like any other and nothing is dropped before the refusal.
            error = DescribeUnwritableElements(arrayProp, jsonKey);
            if (error != null) return false;

            arrayProp.arraySize = elements.Count;

            for (int i = 0; i < elements.Count; i++)
            {
                var elementKey = $"{jsonKey}{ArrayDataMarker}{i}]";
                var elementJson = "{\"" + RestResponse.EscapeJson(elementKey) + "\":" + elements[i] + "}";
                if (!applyElement(
                        arrayProp.GetArrayElementAtIndex(i), elementJson, elementKey,
                        out error, out statusCode))
                    return false;
            }

            error = null;
            return true;
        }

        /// <summary>
        /// Applies every key in <paramref name="requestedKeys"/> that writes an array, appending
        /// each to <paramref name="updated"/>. Keys naming anything else are left alone.
        /// </summary>
        /// <remarks>
        /// Separate from the property walk both callers run, because writing an array changes its
        /// length and a walk in progress is no place to do that -- and because an element address
        /// has to be resolved from its array rather than from wherever the walk reaches. Every key
        /// here was already checked against that walk, so a lookup that fails is a bug rather than
        /// a request to reject, and says so.
        /// </remarks>
        internal static bool TryApplyArrayKeys(
            SerializedObject so,
            string propertiesJson,
            IEnumerable<string> requestedKeys,
            ApplyProperty applyElement,
            List<string> updated,
            out string error,
            out int statusCode)
        {
            error = null;
            statusCode = 400;

            foreach (var key in requestedKeys)
            {
                bool written;
                if (TryParseArrayAddress(key, out var arrayPath, out var address, out var index))
                {
                    var arrayProp = so.FindProperty(arrayPath);
                    if (arrayProp == null || !IsWritableAsArray(arrayProp))
                    {
                        error = $"Property {key} could not be resolved against the array '{arrayPath}'.";
                        statusCode = 500;
                        return false;
                    }

                    written = TryWriteArrayAddress(
                        arrayProp, address, index, propertiesJson, key,
                        applyElement, out error, out statusCode);
                }
                else
                {
                    var arrayProp = so.FindProperty(key);
                    if (arrayProp == null || !IsWritableAsArray(arrayProp)) continue;

                    written = TryWriteArray(
                        arrayProp, propertiesJson, key, applyElement, out error, out statusCode);
                }

                if (!written) return false;
                updated.Add(key);
            }

            return true;
        }

        /// <summary>
        /// Resolves an array address against <paramref name="arrayProp"/> and writes it.
        /// </summary>
        internal static bool TryWriteArrayAddress(
            SerializedProperty arrayProp,
            ArrayAddress address,
            int index,
            string json,
            string jsonKey,
            ApplyProperty applyElement,
            out string error,
            out int statusCode)
        {
            statusCode = 400;

            error = DescribeUnwritableElements(arrayProp, jsonKey);
            if (error != null) return false;

            if (address == ArrayAddress.Size)
            {
                if (!RequestBodyReader.TryGetIntValue(json, jsonKey, out var size, out var present) ||
                    !present)
                {
                    error = Expected(jsonKey, "a JSON integer");
                    return false;
                }
                if (size < 0)
                {
                    error = $"Property {jsonKey} expects a length of zero or more, not {size}.";
                    return false;
                }
                if (!TryBoundLength(size, jsonKey, out error)) return false;

                arrayProp.arraySize = size;
                return true;
            }

            // Range-checked here rather than left to a lookup, which would report an index past the
            // end as a property that does not exist and say nothing about the length.
            if (index >= arrayProp.arraySize)
            {
                error = $"Property {jsonKey} is out of range: the array holds " +
                        $"{arrayProp.arraySize} element(s).";
                return false;
            }

            return applyElement(
                arrayProp.GetArrayElementAtIndex(index), json, jsonKey, out error, out statusCode);
        }

        private static bool TryValidateCompositeObject(
            string json, string jsonKey, string[] allowedMembers, out string error)
        {
            if (RequestBodyReader.TryValidateObjectFields(json, allowedMembers, out var objectError))
            {
                error = null;
                return true;
            }

            error = $"Property {jsonKey} has an invalid JSON object: {objectError}";
            return false;
        }

        private static bool TryReadFloatMember(
            string json,
            string jsonKey,
            string member,
            float currentValue,
            out float value,
            out bool present,
            out string error)
        {
            if (!RequestBodyReader.TryGetFloatValue(json, member, out value, out present))
            {
                error = $"Property {jsonKey}.{member} expects a JSON number.";
                return false;
            }

            if (!present) value = currentValue;
            error = null;
            return true;
        }

        /// <summary>
        /// Returns the first of <paramref name="requestedKeys"/> this object cannot write, or null
        /// when every key is addressable. <paramref name="reason"/> carries the explanation when
        /// the key is unwritable for a reason other than naming nothing.
        /// </summary>
        /// <remarks>
        /// The write loop can only report keys it reached. A key that matches nothing is never
        /// reached at all, so it has to be found by walking the properties once against the keys
        /// the request sent. <paramref name="descendIntoChildren"/> mirrors the caller's own walk,
        /// because a plain key is addressable exactly when that walk would visit the property.
        ///
        /// Matching is on <c>propertyPath</c> alone, which is what both write loops select by. A
        /// child's bare <c>name</c> must not count: <c>x</c> is the name of the child of every
        /// vector property, and accepting it here while the loop looks for <c>m_LocalPosition.x</c>
        /// would let <c>{"x": 5}</c> through the gate and then apply to nothing -- answering 200
        /// for a write that did not happen, which is the whole failure this check exists to end.
        ///
        /// An array address is judged by the array it reaches into rather than by itself. The walk
        /// follows foldout state, so whether it visits an element depends on Editor UI that has
        /// nothing to do with the request -- and on the ScriptableObject endpoint, which does not
        /// descend, it never visits one at all. Addressing the array instead makes both endpoints
        /// answer the same way for the same key.
        /// </remarks>
        internal static string FindUnwritableKey(
            SerializedObject so,
            string propertiesJson,
            bool descendIntoChildren,
            IEnumerable<string> requestedKeys,
            out string reason)
        {
            reason = null;

            var matched = new HashSet<string>(StringComparer.Ordinal);
            var arrays = new HashSet<string>(StringComparer.Ordinal);
            var iter = so.GetIterator();
            var enterChildren = true;
            while (iter.NextVisible(enterChildren))
            {
                enterChildren = descendIntoChildren;
                if (RequestBodyReader.HasTopLevelField(propertiesJson, iter.propertyPath))
                    matched.Add(iter.propertyPath);

                // Every array the walk reaches, not only the ones sent as keys: an element address
                // names its array without the array itself appearing in the request. Its internals
                // are addressed through it rather than walked, so there is nothing below to visit.
                if (!IsWritableAsArray(iter)) continue;
                arrays.Add(iter.propertyPath);
                enterChildren = false;
            }

            // Per array, the key that sets its length and the first key that writes one element.
            // Two elements of one array are a pair of independent writes; a length beside them is
            // not, because it decides how many elements there are to write to.
            var lengthKeyOf  = new Dictionary<string, string>(StringComparer.Ordinal);
            var elementKeyOf = new Dictionary<string, string>(StringComparer.Ordinal);

            foreach (var key in requestedKeys)
            {
                string reachedArray;
                bool setsLength;

                if (TryParseArrayAddress(key, out var arrayPath, out var address, out _))
                {
                    if (!arrays.Contains(arrayPath))
                    {
                        // The array is the part a caller can check, so name it rather than the
                        // element -- whether it is absent or simply not an array.
                        reason = $"No array named '{arrayPath}' on {so.targetObject.GetType().FullName}, " +
                                 $"which '{key}' addresses part of.";
                        return key;
                    }
                    reachedArray = arrayPath;
                    setsLength = address == ArrayAddress.Size;
                }
                else if (!matched.Contains(key))
                {
                    if (NamesArrayInternals(key))
                        reason = $"Key '{key}' reaches inside an array. Only the array itself, " +
                                 "one element as 'name.Array.data[i]', and its length as " +
                                 "'name.Array.size' can be written.";
                    return key;
                }
                else if (arrays.Contains(key))
                {
                    reachedArray = key;
                    setsLength = true; // a whole-array write resizes to the length it carries
                }
                else
                {
                    continue;
                }

                if (lengthKeyOf.TryGetValue(reachedArray, out var earlierLength))
                {
                    reason = ConflictingArrayKeys(earlierLength, key, reachedArray);
                    return key;
                }

                if (setsLength)
                {
                    if (elementKeyOf.TryGetValue(reachedArray, out var earlierElement))
                    {
                        reason = ConflictingArrayKeys(earlierElement, key, reachedArray);
                        return key;
                    }
                    lengthKeyOf.Add(reachedArray, key);
                }
                else if (!elementKeyOf.ContainsKey(reachedArray))
                {
                    elementKeyOf.Add(reachedArray, key);
                }
            }

            return null;
        }

        private static bool TryBoundLength(int length, string jsonKey, out string error)
        {
            if (length <= MaxArrayLength)
            {
                error = null;
                return true;
            }

            error = $"Property {jsonKey} asks for {length} elements, past the limit of " +
                    $"{MaxArrayLength} this endpoint writes.";
            return false;
        }

        private static string ConflictingArrayKeys(string first, string second, string arrayPath)
            => $"Keys '{first}' and '{second}' both write the array '{arrayPath}', and one of them " +
               "sets its length. Send either a length or the elements to write, because which of " +
               "them applies first is not something this endpoint decides for a caller.";

        // ── ObjectReference resolution ────────────────────────────────────────

        private static bool TryResolveObjectReference(
            string json, string jsonKey, SerializedProperty prop,
            out UnityEngine.Object value, out string error, out int statusCode)
        {
            value = null;
            error = null;
            statusCode = 400;

            var rawValue = RequestBodyReader.GetRawValue(json, jsonKey);
            if (rawValue == null)
            {
                // Selected by top-level presence, so an unreadable value is not an absent field.
                // See the matching note in ComponentWriteHandler.TryResolveObjectReference.
                if (RequestBodyReader.HasTopLevelField(json, jsonKey))
                    error = $"Object reference property {jsonKey} is not a well-formed JSON value.";
                return false;
            }

            rawValue = rawValue.Trim();
            if (rawValue == "null")
                return true;

            if (rawValue.Length == 0 || rawValue[0] != '{')
            {
                error = $"Object reference property {jsonKey} must be null or an object.";
                return false;
            }

            if (!RequestBodyReader.TryValidateObjectFields(
                    rawValue, AssetObjectReferenceFields, out var objectError))
            {
                error = $"Invalid object reference property {jsonKey}: {objectError}";
                return false;
            }

            var expectedType = ObjectReferenceResolverUtils.GetManagedObjectType(prop);
            if (!ObjectReferenceResolverUtils.TryReadAssetReferenceFields(
                    rawValue, $"property {jsonKey}",
                    out var assetGuid, out var assetPath, out var requestedType, out var localIdentifier,
                    out error, out statusCode))
                return false;

            if (!string.IsNullOrEmpty(assetGuid) || !string.IsNullOrEmpty(assetPath))
                return ObjectReferenceResolverUtils.TryResolveAssetReference(
                    assetGuid,
                    assetPath,
                    localIdentifier,
                    expectedType,
                    requestedType,
                    $"property {jsonKey}",
                    "Object reference {0} requires assetGuid or assetPath.",
                    "Asset not found for {0} with GUID: {1}",
                    "Asset not found or incompatible for {0}: {1}",
                    "Resolved object for {0} is not assignable to field type {1}.",
                    out value,
                    out error,
                    out statusCode);

            error = $"Object reference property {jsonKey} requires assetGuid or assetPath.";
            return false;
        }
    }
}
