using System;
using System.Globalization;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace ProvingGround.EditorTools
{
    /// <summary>
    /// Sets a named property on a Unity object from a loosely typed JSON value.
    ///
    /// Resolution goes through reflection on the public API rather than through
    /// SerializedObject. That is deliberate: an agent knows the documented names -
    /// <c>fieldOfView</c>, <c>isTrigger</c>, <c>mass</c> - and has no reason to know that
    /// Unity serialises them as <c>m_FieldOfView</c>. SerializedObject is used as a
    /// fallback for the cases reflection cannot reach.
    /// </summary>
    public static class PgPropertyBinder
    {
        const BindingFlags Flags =
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase | BindingFlags.FlattenHierarchy;

        /// <summary>
        /// Applies <paramref name="value"/> to <paramref name="path"/> on <paramref name="target"/>.
        /// Returns null on success, or a description of why it could not be applied.
        /// </summary>
        public static string Set(UnityEngine.Object target, string path, object value)
        {
            if (target == null) return "the target object is null";
            if (string.IsNullOrEmpty(path)) return "no property name was given";

            var segments = path.Split('.');
            object current = target;

            // Walk to the object that owns the final segment, so "material.color" works.
            for (var i = 0; i < segments.Length - 1; i++)
            {
                var member = Resolve(current.GetType(), segments[i]);
                if (member == null) return $"'{segments[i]}' is not a property of {current.GetType().Name}";

                current = Read(member, current);
                if (current == null) return $"'{segments[i]}' is null, so '{path}' cannot be reached";
            }

            var leafName = segments[segments.Length - 1];
            var leaf = Resolve(current.GetType(), leafName);

            if (leaf == null)
                return SetViaSerializedObject(target, path, value)
                       ?? $"'{leafName}' is not a property of {current.GetType().Name}";

            var targetType = leaf is PropertyInfo property ? property.PropertyType : ((FieldInfo)leaf).FieldType;

            object converted;
            try
            {
                converted = Convert(value, targetType);
            }
            catch (Exception e)
            {
                return $"could not read '{value}' as {targetType.Name}: {e.Message}";
            }

            if (converted == null && targetType.IsValueType)
                return $"'{value}' is not a valid {targetType.Name}";

            Undo.RecordObject(target, $"Set {path}");

            try
            {
                Write(leaf, current, converted);
            }
            catch (Exception e)
            {
                return $"setting '{path}' threw: {e.Message}";
            }

            EditorUtility.SetDirty(target);
            return null;
        }

        static MemberInfo Resolve(Type type, string name)
        {
            var property = type.GetProperty(name, Flags);
            if (property != null && property.CanWrite) return property;
            if (property != null && property.CanRead) return property;

            return (MemberInfo)type.GetField(name, Flags);
        }

        static object Read(MemberInfo member, object owner) =>
            member is PropertyInfo property ? property.GetValue(owner) : ((FieldInfo)member).GetValue(owner);

        static void Write(MemberInfo member, object owner, object value)
        {
            if (member is PropertyInfo property)
            {
                if (!property.CanWrite) throw new InvalidOperationException($"'{property.Name}' is read only");
                property.SetValue(owner, value);
                return;
            }

            ((FieldInfo)member).SetValue(owner, value);
        }

        /// <summary>
        /// Last resort for properties with no public setter. Tries both the given name and
        /// Unity's <c>m_</c> convention.
        /// </summary>
        static string SetViaSerializedObject(UnityEngine.Object target, string path, object value)
        {
            using var serialized = new SerializedObject(target);
            var property = serialized.FindProperty(path)
                           ?? serialized.FindProperty("m_" + char.ToUpperInvariant(path[0]) + path.Substring(1));

            if (property == null) return null;

            try
            {
                switch (property.propertyType)
                {
                    case SerializedPropertyType.Float: property.floatValue = System.Convert.ToSingle(Raw(value)); break;
                    case SerializedPropertyType.Integer: property.intValue = System.Convert.ToInt32(Raw(value)); break;
                    case SerializedPropertyType.Boolean: property.boolValue = System.Convert.ToBoolean(Raw(value)); break;
                    case SerializedPropertyType.String: property.stringValue = Raw(value)?.ToString(); break;
                    case SerializedPropertyType.Color: property.colorValue = (Color)Convert(value, typeof(Color)); break;
                    case SerializedPropertyType.Vector3: property.vector3Value = (Vector3)Convert(value, typeof(Vector3)); break;
                    case SerializedPropertyType.Vector2: property.vector2Value = (Vector2)Convert(value, typeof(Vector2)); break;
                    case SerializedPropertyType.Enum: property.enumValueIndex = System.Convert.ToInt32(Raw(value)); break;
                    case SerializedPropertyType.ObjectReference:
                        property.objectReferenceValue = (UnityEngine.Object)Convert(value, typeof(UnityEngine.Object));
                        break;
                    default:
                        return $"'{path}' is a {property.propertyType}, which is not supported yet";
                }
            }
            catch (Exception e)
            {
                return $"setting '{path}' threw: {e.Message}";
            }

            serialized.ApplyModifiedProperties();
            EditorUtility.SetDirty(target);
            return "";
        }

        /// <summary>Unwraps a Newtonsoft token to a plain CLR value.</summary>
        static object Raw(object value) => value is JValue jValue ? jValue.Value : value;

        /// <summary>Converts a JSON-ish value to <paramref name="targetType"/>.</summary>
        public static object Convert(object value, Type targetType)
        {
            if (value == null) return null;

            if (targetType == typeof(string)) return Raw(value)?.ToString();

            if (targetType.IsEnum)
            {
                var raw = Raw(value);
                return raw is string name
                    ? Enum.Parse(targetType, name, true)
                    : Enum.ToObject(targetType, System.Convert.ToInt32(raw));
            }

            if (targetType == typeof(Color) || targetType == typeof(Color32))
            {
                var color = ToColor(value);
                return targetType == typeof(Color32) ? (object)(Color32)color : color;
            }

            if (targetType == typeof(Vector2)) { var v = ToFloats(value, 2); return new Vector2(v[0], v[1]); }
            if (targetType == typeof(Vector3)) { var v = ToFloats(value, 3); return new Vector3(v[0], v[1], v[2]); }
            if (targetType == typeof(Vector4)) { var v = ToFloats(value, 4); return new Vector4(v[0], v[1], v[2], v[3]); }
            if (targetType == typeof(Quaternion)) { var v = ToFloats(value, 3); return Quaternion.Euler(v[0], v[1], v[2]); }

            if (typeof(UnityEngine.Object).IsAssignableFrom(targetType))
                return ToUnityObject(Raw(value)?.ToString(), targetType);

            if (targetType == typeof(LayerMask))
            {
                var raw = Raw(value);
                return raw is string layerName
                    ? (LayerMask)(1 << LayerMask.NameToLayer(layerName))
                    : (LayerMask)System.Convert.ToInt32(raw);
            }

            var plain = Raw(value);
            return plain == null ? null : System.Convert.ChangeType(plain, targetType, CultureInfo.InvariantCulture);
        }

        static Color ToColor(object value)
        {
            var raw = Raw(value);
            if (raw is string text)
            {
                if (ColorUtility.TryParseHtmlString(text, out var parsed)) return parsed;
                throw new FormatException($"'{text}' is not a colour");
            }

            var floats = ToFloats(value, 4, 1f);
            return new Color(floats[0], floats[1], floats[2], floats[3]);
        }

        static float[] ToFloats(object value, int count, float fill = 0f)
        {
            var result = Enumerable.Repeat(fill, count).ToArray();

            switch (value)
            {
                case JArray array:
                    for (var i = 0; i < Mathf.Min(count, array.Count); i++) result[i] = array[i].Value<float>();
                    return result;

                case JObject json:
                    var keys = new[] { "x", "y", "z", "w" };
                    for (var i = 0; i < count; i++)
                        if (json[keys[i]] != null)
                            result[i] = json[keys[i]].Value<float>();
                    return result;

                case float[] floats:
                    for (var i = 0; i < Mathf.Min(count, floats.Length); i++) result[i] = floats[i];
                    return result;

                case System.Collections.IEnumerable enumerable when !(value is string):
                    var index = 0;
                    foreach (var item in enumerable)
                    {
                        if (index >= count) break;
                        result[index++] = System.Convert.ToSingle(Raw(item), CultureInfo.InvariantCulture);
                    }

                    return result;

                default:
                    // A single number fills every channel, which is what someone means by
                    // "scale: 2".
                    var single = System.Convert.ToSingle(Raw(value), CultureInfo.InvariantCulture);
                    for (var i = 0; i < count; i++) result[i] = single;
                    return result;
            }
        }

        static UnityEngine.Object ToUnityObject(string reference, Type targetType)
        {
            if (string.IsNullOrEmpty(reference)) return null;

            // An asset path is unambiguous, so try it first.
            if (reference.StartsWith("Assets/") || reference.StartsWith("Packages/"))
            {
                var asset = AssetDatabase.LoadAssetAtPath(reference, targetType);
                if (asset != null) return asset;
            }

            var found = PgLocate.Find(reference);
            if (found == null) return null;

            if (targetType == typeof(GameObject)) return found.gameObject;
            if (typeof(Component).IsAssignableFrom(targetType)) return found.GetComponent(targetType);
            return found.gameObject;
        }
    }
}
