using System;
using System.Reflection;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace VividRP.AgenticDebugger
{
    // Keep Unity's private API in one adapter. Missing members fail before capture starts.
    internal sealed class FrameDebuggerApi
    {
        private const BindingFlags Static = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        private readonly Type utility, eventData;
        private readonly MethodInfo getEvents, getData, getName;
        internal readonly Func<bool> Enabled, Local, Supported;
        internal readonly Func<int> Count, Limit, EventsHash, Remote;
        internal readonly Action<int> SetLimit;
        internal readonly Action<bool, int> SetEnabled;
        internal readonly Action Repaint;
        internal readonly Action EnterCapture, LeaveCapture;

        internal FrameDebuggerApi()
        {
            var editor = typeof(Editor).Assembly;
            utility = editor.GetType("UnityEditorInternal.FrameDebuggerInternal.FrameDebuggerUtility", true);
            eventData = editor.GetType("UnityEditorInternal.FrameDebuggerInternal.FrameDebuggerEventData", true);
            var state = typeof(UnityEngine.Object).Assembly.GetType("UnityEngine.FrameDebugger", true);
            Enabled = Getter<bool>(state, "enabled");
            Local = Bind<Func<bool>>(state, "IsLocalEnabled");
            Supported = Getter<bool>(utility, "locallySupported");
            Count = Getter<int>(utility, "count");
            Limit = Getter<int>(utility, "limit");
            EventsHash = Getter<int>(utility, "eventsHash");
            Remote = Bind<Func<int>>(utility, "GetRemotePlayerGUID");
            SetLimit = (Action<int>)Delegate.CreateDelegate(typeof(Action<int>), utility.GetProperty("limit", Static).GetSetMethod(true));
            SetEnabled = Bind<Action<bool, int>>(utility, "SetEnabled");
            Repaint = Bind<Action>(typeof(EditorApplication), "SetSceneRepaintDirty");
            getEvents = Required(utility, "GetFrameEvents");
            getData = Required(utility, "GetFrameEventData");
            getName = Required(utility, "GetFrameEventInfoName");
            if (utility.GetMethod("EnterCapturingScope", Static) != null && utility.GetMethod("LeaveCapturingScope", Static) != null)
            {
                EnterCapture = Bind<Action>(utility, "EnterCapturingScope");
                LeaveCapture = Bind<Action>(utility, "LeaveCapturingScope");
            }
            if (eventData.GetField("m_FrameEventIndex") == null)
                throw new MissingFieldException(eventData.FullName, "m_FrameEventIndex");
        }

        private static MethodInfo Required(Type type, string name) =>
            type.GetMethod(name, Static) ?? throw new MissingMethodException(type.FullName, name);

        private static T Bind<T>(Type type, string name) where T : Delegate =>
            (T)Delegate.CreateDelegate(typeof(T), Required(type, name));

        private static Func<T> Getter<T>(Type type, string name) =>
            (Func<T>)Delegate.CreateDelegate(typeof(Func<T>),
                type.GetProperty(name, Static)?.GetGetMethod(true) ?? throw new MissingMemberException(type.FullName, name));

        internal Array Events() => (Array)getEvents.Invoke(null, null);
        internal string Name(int index) => (string)getName.Invoke(null, new object[] { index });

        internal static bool Inspectable(object frameEvent)
        {
            string kind = frameEvent.GetType().GetField("m_Type").GetValue(frameEvent).ToString();
            return kind == "ComputeDispatch" || kind == "RayTracingDispatch";
        }

        internal static JObject DescribeDispatch(object data, bool includeShaderProperties)
        {
            var result = new JObject();
            foreach (var field in data.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                // Do not touch native render-pass targets or raster state on affected Unity versions.
                if (field.Name == "m_FrameEventIndex" || field.Name.StartsWith("m_ComputeShader", StringComparison.Ordinal)
                    || field.Name.StartsWith("m_RayTracing", StringComparison.Ordinal)
                    || (includeShaderProperties && field.Name == "m_ShaderInfo"))
                    result[field.Name] = Describe(field.GetValue(data));
            }
            return result;
        }

        internal object Data(int index)
        {
            object data = Activator.CreateInstance(eventData);
            if (!(bool)getData.Invoke(null, new[] { (object)index, data })) return null;
            return (int)eventData.GetField("m_FrameEventIndex").GetValue(data) == index ? data : null;
        }

        // Only public fields of native DTOs, never Unity Object property graphs.
        internal static JToken Describe(object value)
        {
            if (value == null) return JValue.CreateNull();
            var type = value.GetType();
            if (type.IsEnum) return new JValue(value.ToString());
            if (type.IsPrimitive || value is string || value is decimal) return new JValue(value);
            if (value is UnityEngine.Object obj)
            {
                if (!obj) return JValue.CreateNull();
                var result = new JObject { ["name"] = obj.name, ["type"] = type.FullName };
#if UNITY_6000_7_OR_NEWER
                result["entityId"] = obj.GetEntityId().ToString();
#else
                result["instanceId"] = obj.GetInstanceID();
#endif
                if (obj is Texture texture)
                {
                    result["width"] = texture.width;
                    result["height"] = texture.height;
                    result["dimension"] = texture.dimension.ToString();
                    result["graphicsFormat"] = texture.graphicsFormat.ToString();
                }
                return result;
            }
            if (value is Array array)
            {
                var result = new JArray();
                foreach (object item in array) result.Add(Describe(item));
                return result;
            }
            var fields = type.GetFields(BindingFlags.Instance | BindingFlags.Public);
            if (fields.Length == 0) return new JValue(value.ToString());
            var record = new JObject();
            foreach (var field in fields) record[field.Name] = Describe(field.GetValue(value));
            return record;
        }
    }
}
