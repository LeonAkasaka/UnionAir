using System;
using System.Collections;
using System.Net;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace LeonAkasaka.UnionAir.Editor
{
    /// <summary>
    /// Handles Play mode interactions with Unity UI (uGUI) elements.
    /// </summary>
    internal class PlayModeUiHandler
    {
        public void HandleElements(HttpListenerRequest request, HttpListenerResponse response)
            => UnityUiInteractionBackend.HandleElements(request, response);

        public void HandleClick(HttpListenerRequest request, HttpListenerResponse response)
            => UnityUiInteractionBackend.HandleClick(request, response);

        public void HandleText(HttpListenerRequest request, HttpListenerResponse response)
            => UnityUiInteractionBackend.HandleText(request, response);

        public void HandleScroll(HttpListenerRequest request, HttpListenerResponse response)
            => UnityUiInteractionBackend.HandleScroll(request, response);

        public void HandleValue(HttpListenerRequest request, HttpListenerResponse response)
            => UnityUiInteractionBackend.HandleValue(request, response);
    }

    internal static class UnityUiInteractionBackend
    {
        private const string UnityUiBackend = "unityUi";

        // Resolved once per domain load; null when TextMeshPro is not installed.
        private static readonly Type TmpInputFieldType = ResolveTmpType("TMPro.TMP_InputField");
        private static readonly Type TmpDropdownType = ResolveTmpType("TMPro.TMP_Dropdown");

        private static Type ResolveTmpType(string fullName)
            => Type.GetType(fullName + ", Unity.TextMeshPro") ?? ObjectRefUtils.ResolveType(fullName, typeof(Component));

        public static void HandleElements(HttpListenerRequest request, HttpListenerResponse response)
        {
            if (!EnsurePlaying(response)) return;
            if (!SceneResolver.TryResolveFromRequest(request, response, null, out var scene)) return;

            var sb = new StringBuilder();
            var count = 0;
            sb.Append("{\"backend\":\"unityUi\",\"elements\":[");
            AppendElements<Button>(scene, sb, ref count, AppendButtonFields);
            AppendElements<InputField>(scene, sb, ref count, AppendInputFieldFields);
            AppendElements<ScrollRect>(scene, sb, ref count, AppendScrollRectFields);
            AppendElements<Toggle>(scene, sb, ref count, AppendToggleFields);
            AppendElements<Slider>(scene, sb, ref count, AppendSliderFields);
            AppendElements<Dropdown>(scene, sb, ref count, AppendDropdownFields);
            AppendTmpElements(scene, sb, ref count, TmpInputFieldType, AppendTmpInputFieldFields);
            AppendTmpElements(scene, sb, ref count, TmpDropdownType, AppendTmpDropdownFields);
            sb.Append("],\"count\":").Append(count).Append("}");
            RestResponse.Send(response, sb.ToString());
        }

        public static void HandleClick(HttpListenerRequest request, HttpListenerResponse response)
        {
            if (!EnsurePlaying(response)) return;
            var body = RequestBodyReader.ReadString(request);
            if (!EnsureBackend(body, response)) return;
            if (!EnsureEventSystem(response)) return;

            if (!TryResolveTarget(request, response, body, "click", out var go, out var component)) return;
            var clickHandler = ResolveClickHandler(go, component);
            if (clickHandler == null)
            {
                RestResponse.SendError(response, $"target does not resolve to a Button or IPointerClickHandler: {GameObjectUtils.GetPath(go)}", 422);
                return;
            }

            var targetComponent = clickHandler as Component;
            if (targetComponent == null)
            {
                RestResponse.SendError(response, "Resolved click target is not a Component.", 422);
                return;
            }

            if (targetComponent is Selectable selectable && !selectable.IsInteractable())
            {
                RestResponse.SendError(response, $"UI element is not interactable: {GameObjectUtils.GetPath(targetComponent.gameObject)}", 409);
                return;
            }

            var pointer = CreatePointerEvent(targetComponent.gameObject, RequestBodyReader.GetObject(body, "normalizedPosition"));
            ExecuteEvents.Execute(targetComponent.gameObject, pointer, ExecuteEvents.pointerDownHandler);
            ExecuteEvents.Execute(targetComponent.gameObject, pointer, ExecuteEvents.pointerUpHandler);
            ExecuteEvents.Execute(targetComponent.gameObject, pointer, ExecuteEvents.pointerClickHandler);

            SendInteractionResponse(response, "click", targetComponent, "\"clicked\":true");
        }

        public static void HandleText(HttpListenerRequest request, HttpListenerResponse response)
        {
            if (!EnsurePlaying(response)) return;
            var body = RequestBodyReader.ReadString(request);
            if (!EnsureBackend(body, response)) return;
            if (!EnsureEventSystem(response)) return;

            var text = RequestBodyReader.GetString(body, "text");
            if (text == null)
            {
                RestResponse.SendError(response, "Required field 'text' is missing.", 400);
                return;
            }

            if (!TryResolveTarget(request, response, body, "text", out var go, out var component)) return;
            var input = ResolveComponent<InputField>(go, component);
            if (input != null)
            {
                if (!input.IsInteractable())
                {
                    RestResponse.SendError(response, $"InputField is not interactable: {GameObjectUtils.GetPath(input.gameObject)}", 409);
                    return;
                }

                input.text = text;
                if (RequestBodyReader.GetBool(body, "submit") == true)
                {
                    input.onEndEdit.Invoke(input.text);
                }

                SendInteractionResponse(response, "text", input, $"\"text\":\"{RestResponse.EscapeJson(input.text)}\"");
                return;
            }

            var tmpInput = ResolveComponentByType(go, component, TmpInputFieldType);
            if (tmpInput == null)
            {
                RestResponse.SendError(response, $"target does not resolve to a UnityEngine.UI.InputField or TMPro.TMP_InputField: {GameObjectUtils.GetPath(go)}", 422);
                return;
            }
            if (!IsSelectableInteractable(tmpInput))
            {
                RestResponse.SendError(response, $"TMP_InputField is not interactable: {GameObjectUtils.GetPath(tmpInput.gameObject)}", 409);
                return;
            }

            SetProperty(tmpInput, "text", text);
            if (RequestBodyReader.GetBool(body, "submit") == true)
            {
                InvokeUnityStringEvent(tmpInput, "onSubmit", text);
                InvokeUnityStringEvent(tmpInput, "onEndEdit", text);
            }

            var currentText = GetStringProperty(tmpInput, "text") ?? text;
            SendInteractionResponse(response, "text", tmpInput, $"\"text\":\"{RestResponse.EscapeJson(currentText)}\"");
        }

        public static void HandleScroll(HttpListenerRequest request, HttpListenerResponse response)
        {
            if (!EnsurePlaying(response)) return;
            var body = RequestBodyReader.ReadString(request);
            if (!EnsureBackend(body, response)) return;
            if (!EnsureEventSystem(response)) return;

            if (!TryResolveTarget(request, response, body, "scroll", out var go, out var component)) return;
            var scrollRect = ResolveComponent<ScrollRect>(go, component);
            if (scrollRect == null)
            {
                RestResponse.SendError(response, $"target does not resolve to a UnityEngine.UI.ScrollRect: {GameObjectUtils.GetPath(go)}", 422);
                return;
            }
            if (!scrollRect.enabled || !scrollRect.gameObject.activeInHierarchy)
            {
                RestResponse.SendError(response, $"ScrollRect is not active: {GameObjectUtils.GetPath(scrollRect.gameObject)}", 409);
                return;
            }

            var normalized = RequestBodyReader.GetObject(body, "normalizedPosition");
            var delta = RequestBodyReader.GetObject(body, "delta");
            if (normalized != null)
            {
                var x = RequestBodyReader.GetFloat(normalized, "x");
                var y = RequestBodyReader.GetFloat(normalized, "y");
                if (!x.HasValue && !y.HasValue)
                {
                    RestResponse.SendError(response, "normalizedPosition must include 'x' or 'y'.", 400);
                    return;
                }
                if (x.HasValue) scrollRect.horizontalNormalizedPosition = Mathf.Clamp01(x.Value);
                if (y.HasValue) scrollRect.verticalNormalizedPosition = Mathf.Clamp01(y.Value);
            }
            else if (delta != null)
            {
                var x = RequestBodyReader.GetFloat(delta, "x") ?? 0f;
                var y = RequestBodyReader.GetFloat(delta, "y") ?? 0f;
                var pointer = CreatePointerEvent(scrollRect.gameObject, null);
                pointer.scrollDelta = new Vector2(x, y);
                ExecuteEvents.Execute(scrollRect.gameObject, pointer, ExecuteEvents.scrollHandler);
            }
            else
            {
                RestResponse.SendError(response, "Provide either 'delta' or 'normalizedPosition'.", 400);
                return;
            }

            SendInteractionResponse(response, "scroll", scrollRect,
                $"\"normalizedPosition\":{{\"x\":{RestResponse.FormatFloat(scrollRect.horizontalNormalizedPosition)},\"y\":{RestResponse.FormatFloat(scrollRect.verticalNormalizedPosition)}}}");
        }

        public static void HandleValue(HttpListenerRequest request, HttpListenerResponse response)
        {
            if (!EnsurePlaying(response)) return;
            var body = RequestBodyReader.ReadString(request);
            if (!EnsureBackend(body, response)) return;
            if (!EnsureEventSystem(response)) return;

            if (!TryResolveTarget(request, response, body, "value", out var go, out var component)) return;

            var toggle = ResolveComponent<Toggle>(go, component);
            if (toggle != null)
            {
                var value = RequestBodyReader.GetBool(body, "value");
                if (!value.HasValue)
                {
                    RestResponse.SendError(response, "Toggle value must be a boolean.", 400);
                    return;
                }
                if (!toggle.IsInteractable())
                {
                    RestResponse.SendError(response, $"Toggle is not interactable: {GameObjectUtils.GetPath(toggle.gameObject)}", 409);
                    return;
                }
                toggle.isOn = value.Value;
                SendInteractionResponse(response, "value", toggle, $"\"value\":{RestResponse.FormatBool(toggle.isOn)}");
                return;
            }

            var slider = ResolveComponent<Slider>(go, component);
            if (slider != null)
            {
                var value = RequestBodyReader.GetFloat(body, "value");
                if (!value.HasValue)
                {
                    RestResponse.SendError(response, "Slider value must be a number.", 400);
                    return;
                }
                if (!slider.IsInteractable())
                {
                    RestResponse.SendError(response, $"Slider is not interactable: {GameObjectUtils.GetPath(slider.gameObject)}", 409);
                    return;
                }
                slider.value = Mathf.Clamp(value.Value, slider.minValue, slider.maxValue);
                SendInteractionResponse(response, "value", slider, $"\"value\":{RestResponse.FormatFloat(slider.value)}");
                return;
            }

            var dropdown = ResolveComponent<Dropdown>(go, component);
            if (dropdown != null)
            {
                var value = RequestBodyReader.GetInt(body, "value");
                if (!value.HasValue)
                {
                    RestResponse.SendError(response, "Dropdown value must be an integer.", 400);
                    return;
                }
                if (!dropdown.IsInteractable())
                {
                    RestResponse.SendError(response, $"Dropdown is not interactable: {GameObjectUtils.GetPath(dropdown.gameObject)}", 409);
                    return;
                }
                var max = dropdown.options == null ? -1 : dropdown.options.Count - 1;
                if (value.Value < 0 || value.Value > max)
                {
                    RestResponse.SendError(response, $"Dropdown value is out of range: {value.Value}", 400);
                    return;
                }
                dropdown.value = value.Value;
                dropdown.RefreshShownValue();
                SendInteractionResponse(response, "value", dropdown, $"\"value\":{dropdown.value}");
                return;
            }

            var tmpDropdown = ResolveComponentByType(go, component, TmpDropdownType);
            if (tmpDropdown != null)
            {
                var value = RequestBodyReader.GetInt(body, "value");
                if (!value.HasValue)
                {
                    RestResponse.SendError(response, "TMP_Dropdown value must be an integer.", 400);
                    return;
                }
                if (!IsSelectableInteractable(tmpDropdown))
                {
                    RestResponse.SendError(response, $"TMP_Dropdown is not interactable: {GameObjectUtils.GetPath(tmpDropdown.gameObject)}", 409);
                    return;
                }
                var optionCount = GetOptionsCount(tmpDropdown);
                if (value.Value < 0 || (optionCount >= 0 && value.Value >= optionCount))
                {
                    RestResponse.SendError(response, $"TMP_Dropdown value is out of range: {value.Value}", 400);
                    return;
                }
                SetProperty(tmpDropdown, "value", value.Value);
                InvokeMethod(tmpDropdown, "RefreshShownValue");
                var currentValue = GetIntProperty(tmpDropdown, "value", value.Value);
                SendInteractionResponse(response, "value", tmpDropdown, $"\"value\":{currentValue}");
                return;
            }

            RestResponse.SendError(response, $"target does not resolve to a Toggle, Slider, Dropdown, or TMP_Dropdown: {GameObjectUtils.GetPath(go)}", 422);
        }

        private static bool EnsurePlaying(HttpListenerResponse response)
        {
            if (EditorApplication.isPlaying) return true;
            RestResponse.SendError(response, "Not in Play mode.", 409);
            return false;
        }

        private static bool EnsureBackend(string body, HttpListenerResponse response)
        {
            var backend = RequestBodyReader.GetString(body, "backend");
            if (string.IsNullOrEmpty(backend) || string.Equals(backend, UnityUiBackend, StringComparison.OrdinalIgnoreCase))
                return true;

            RestResponse.SendError(response, $"Unsupported UI backend: '{backend}'. Supported backends: unityUi.", 400);
            return false;
        }

        private static bool EnsureEventSystem(HttpListenerResponse response)
        {
            if (EventSystem.current != null) return true;
            RestResponse.SendError(response, "No active EventSystem exists in the scene.", 409);
            return false;
        }

        private static bool TryResolveTarget(
            HttpListenerRequest request,
            HttpListenerResponse response,
            string body,
            string operation,
            out GameObject go,
            out Component component)
        {
            go = null;
            component = null;

            if (!SceneResolver.TryResolveFromRequest(request, response, body, out var scene))
                return false;

            if (!ObjectRefUtils.TryReadBody(body, "target", out var target, out var error, out var statusCode) ||
                !ObjectRefUtils.TryResolveGameObjectOrComponent(scene, target, "target", out go, out component, out error, out statusCode))
            {
                RestResponse.SendError(response, error, statusCode);
                return false;
            }

            if (go == null && component != null)
                go = component.gameObject;

            if (go == null)
            {
                RestResponse.SendError(response, $"target could not be resolved for {operation}.", 404);
                return false;
            }

            return true;
        }

        private static IPointerClickHandler ResolveClickHandler(GameObject go, Component component)
        {
            if (component is IPointerClickHandler direct)
                return direct;

            var button = ResolveComponent<Button>(go, component);
            if (button != null)
                return button;

            var components = go.GetComponents<Component>();
            foreach (var candidate in components)
            {
                if (candidate is IPointerClickHandler handler)
                    return handler;
            }
            return null;
        }

        private static T ResolveComponent<T>(GameObject go, Component component) where T : Component
        {
            if (component != null)
                return component as T;
            return go == null ? null : go.GetComponent<T>();
        }

        private static Component ResolveComponentByType(GameObject go, Component component, Type type)
        {
            if (type == null) return null;
            if (component != null)
                return type.IsInstanceOfType(component) ? component : null;
            return go == null ? null : go.GetComponent(type);
        }

        private static bool IsSelectableInteractable(Component component)
        {
            if (component is Selectable selectable)
                return selectable.IsInteractable();
            var method = component.GetType().GetMethod("IsInteractable", BindingFlags.Instance | BindingFlags.Public);
            if (method != null && method.ReturnType == typeof(bool))
                return (bool)method.Invoke(component, null);
            return (!(component is Behaviour behaviour) || behaviour.enabled) &&
                   component.gameObject.activeInHierarchy;
        }

        private static PointerEventData CreatePointerEvent(GameObject target, string normalizedPositionJson)
        {
            var pointer = new PointerEventData(EventSystem.current)
            {
                button = PointerEventData.InputButton.Left,
                clickCount = 1,
                eligibleForClick = true,
                pointerPress = target,
                pointerCurrentRaycast = new RaycastResult { gameObject = target }
            };

            pointer.position = GetScreenPosition(target, normalizedPositionJson);
            return pointer;
        }

        private static Vector2 GetScreenPosition(GameObject target, string normalizedPositionJson)
        {
            var normalized = new Vector2(
                RequestBodyReader.GetFloat(normalizedPositionJson, "x") ?? 0.5f,
                RequestBodyReader.GetFloat(normalizedPositionJson, "y") ?? 0.5f);
            normalized.x = Mathf.Clamp01(normalized.x);
            normalized.y = Mathf.Clamp01(normalized.y);

            var rectTransform = target.GetComponent<RectTransform>();
            if (rectTransform == null)
                return Vector2.zero;

            var rect = rectTransform.rect;
            var local = new Vector3(
                Mathf.Lerp(rect.xMin, rect.xMax, normalized.x),
                Mathf.Lerp(rect.yMin, rect.yMax, normalized.y),
                0f);
            var world = rectTransform.TransformPoint(local);
            return RectTransformUtility.WorldToScreenPoint(GetEventCamera(target), world);
        }

        private static Camera GetEventCamera(GameObject target)
        {
            var canvas = target.GetComponentInParent<Canvas>();
            if (canvas == null || canvas.renderMode == RenderMode.ScreenSpaceOverlay)
                return null;
            return canvas.worldCamera != null ? canvas.worldCamera : Camera.main;
        }

        private static void SendInteractionResponse(HttpListenerResponse response, string action, Component component, string fields)
        {
            var go = component.gameObject;
            RestResponse.Send(response,
                $"{{\"success\":true,\"backend\":\"unityUi\",\"action\":\"{action}\",\"path\":\"{RestResponse.EscapeJson(GameObjectUtils.GetPath(go))}\",\"globalObjectId\":\"{RestResponse.EscapeJson(ObjectIdUtils.GetGlobalObjectId(go))}\",\"component\":\"{RestResponse.EscapeJson(component.GetType().FullName)}\",\"componentGlobalObjectId\":\"{RestResponse.EscapeJson(ObjectIdUtils.GetGlobalObjectId(component))}\",{fields}}}");
        }

        private static void AppendElements<T>(
            UnityEngine.SceneManagement.Scene scene,
            StringBuilder sb,
            ref int count,
            Action<StringBuilder, T> appendFields) where T : Component
        {
            foreach (var component in UnityEngine.Object.FindObjectsByType<T>(FindObjectsInactive.Exclude))
            {
                if (component == null || component.gameObject.scene != scene)
                    continue;

                if (count > 0) sb.Append(',');
                count++;
                AppendElementBase(sb, component);
                appendFields(sb, component);
                sb.Append('}');
            }
        }

        private static void AppendTmpElements(
            UnityEngine.SceneManagement.Scene scene,
            StringBuilder sb,
            ref int count,
            Type type,
            Action<StringBuilder, Component> appendFields)
        {
            if (type == null) return;

            foreach (var obj in UnityEngine.Object.FindObjectsByType(type, FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                var component = obj as Component;
                if (component == null || component.gameObject.scene != scene)
                    continue;

                if (count > 0) sb.Append(',');
                count++;
                AppendElementBase(sb, component);
                appendFields(sb, component);
                sb.Append('}');
            }
        }

        private static void AppendElementBase(StringBuilder sb, Component component)
        {
            var go = component.gameObject;
            sb.Append('{');
            sb.Append($"\"path\":\"{RestResponse.EscapeJson(GameObjectUtils.GetPath(go))}\",");
            sb.Append($"\"globalObjectId\":\"{RestResponse.EscapeJson(ObjectIdUtils.GetGlobalObjectId(go))}\",");
            sb.Append($"\"componentGlobalObjectId\":\"{RestResponse.EscapeJson(ObjectIdUtils.GetGlobalObjectId(component))}\",");
            sb.Append($"\"type\":\"{RestResponse.EscapeJson(component.GetType().FullName)}\",");
        }

        private static void AppendSelectableFields(StringBuilder sb, Selectable selectable)
        {
            sb.Append($"\"interactable\":{RestResponse.FormatBool(selectable.IsInteractable())}");
        }

        private static void AppendButtonFields(StringBuilder sb, Button button)
        {
            AppendSelectableFields(sb, button);
        }

        private static void AppendInputFieldFields(StringBuilder sb, InputField input)
        {
            AppendSelectableFields(sb, input);
            sb.Append($",\"text\":\"{RestResponse.EscapeJson(input.text)}\"");
        }

        private static void AppendScrollRectFields(StringBuilder sb, ScrollRect scrollRect)
        {
            sb.Append($"\"interactable\":{RestResponse.FormatBool(scrollRect.enabled && scrollRect.gameObject.activeInHierarchy)},");
            sb.Append($"\"normalizedPosition\":{{\"x\":{RestResponse.FormatFloat(scrollRect.horizontalNormalizedPosition)},\"y\":{RestResponse.FormatFloat(scrollRect.verticalNormalizedPosition)}}}");
        }

        private static void AppendToggleFields(StringBuilder sb, Toggle toggle)
        {
            AppendSelectableFields(sb, toggle);
            sb.Append($",\"value\":{RestResponse.FormatBool(toggle.isOn)}");
        }

        private static void AppendSliderFields(StringBuilder sb, Slider slider)
        {
            AppendSelectableFields(sb, slider);
            sb.Append($",\"value\":{RestResponse.FormatFloat(slider.value)},\"minValue\":{RestResponse.FormatFloat(slider.minValue)},\"maxValue\":{RestResponse.FormatFloat(slider.maxValue)}");
        }

        private static void AppendDropdownFields(StringBuilder sb, Dropdown dropdown)
        {
            AppendSelectableFields(sb, dropdown);
            sb.Append($",\"value\":{dropdown.value},\"optionCount\":{(dropdown.options == null ? 0 : dropdown.options.Count)}");
        }

        private static void AppendTmpInputFieldFields(StringBuilder sb, Component input)
        {
            sb.Append($"\"interactable\":{RestResponse.FormatBool(IsSelectableInteractable(input))}");
            var text = GetStringProperty(input, "text") ?? "";
            sb.Append($",\"text\":\"{RestResponse.EscapeJson(text)}\"");
        }

        private static void AppendTmpDropdownFields(StringBuilder sb, Component dropdown)
        {
            sb.Append($"\"interactable\":{RestResponse.FormatBool(IsSelectableInteractable(dropdown))}");
            var value = GetIntProperty(dropdown, "value", 0);
            sb.Append($",\"value\":{value},\"optionCount\":{Math.Max(0, GetOptionsCount(dropdown))}");
        }

        private static string GetStringProperty(Component component, string propertyName)
        {
            var property = component.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public);
            return property == null ? null : property.GetValue(component, null) as string;
        }

        private static int GetIntProperty(Component component, string propertyName, int fallback)
        {
            var property = component.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public);
            if (property == null || property.PropertyType != typeof(int)) return fallback;
            return (int)property.GetValue(component, null);
        }

        private static void SetProperty(Component component, string propertyName, object value)
        {
            var property = component.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public);
            if (property != null && property.CanWrite)
                property.SetValue(component, value, null);
        }

        private static void InvokeMethod(Component component, string methodName)
        {
            var method = component.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public, null, Type.EmptyTypes, null);
            if (method != null)
                method.Invoke(component, null);
        }

        private static void InvokeUnityStringEvent(Component component, string propertyName, string value)
        {
            var property = component.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public);
            var unityEvent = property == null ? null : property.GetValue(component, null);
            if (unityEvent == null)
            {
                var field = component.GetType().GetField(propertyName, BindingFlags.Instance | BindingFlags.Public);
                unityEvent = field == null ? null : field.GetValue(component);
            }
            var invoke = unityEvent?.GetType().GetMethod("Invoke", new[] { typeof(string) });
            if (invoke != null)
                invoke.Invoke(unityEvent, new object[] { value });
        }

        private static int GetOptionsCount(Component component)
        {
            var property = component.GetType().GetProperty("options", BindingFlags.Instance | BindingFlags.Public);
            var options = property == null ? null : property.GetValue(component, null) as ICollection;
            return options == null ? -1 : options.Count;
        }
    }
}
