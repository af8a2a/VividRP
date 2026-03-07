using System.Reflection;
using UnityEngine.Rendering.RenderGraphModule;
using VividRP.Runtime;

namespace VividRP.Editor.RenderGraph
{
    internal static class RenderPassPortUtility
    {
        private const string InputPortSuffix = "_In";
        private const string OutputPortSuffix = "_Out";

        internal static bool CanRead(AccessFlags access)
        {
            return (access & AccessFlags.Read) != 0;
        }

        internal static bool CanWrite(AccessFlags access)
        {
            return (access & AccessFlags.Write) != 0;
        }

        internal static string GetInputPortName(string fieldName, AccessFlags access)
        {
            if (!CanRead(access) && !CanWrite(access))
                return null;

            return !CanRead(access) && CanWrite(access)
                ? $"{fieldName}{InputPortSuffix}"
                : CanWrite(access)
                    ? $"{fieldName}{InputPortSuffix}"
                    : fieldName;
        }

        internal static string GetOutputPortName(string fieldName, AccessFlags access)
        {
            if (!CanWrite(access))
                return null;

            return CanRead(access) ? $"{fieldName}{OutputPortSuffix}" : fieldName;
        }

        internal static AccessFlags GetInputPortDisplayAccess(AccessFlags access)
        {
            if (CanRead(access) && CanWrite(access))
                return AccessFlags.Read;

            if (CanRead(access))
                return AccessFlags.Read;

            if (CanWrite(access))
                return AccessFlags.Write;

            return access;
        }

        internal static AccessFlags GetOutputPortDisplayAccess(AccessFlags access)
        {
            if (CanWrite(access))
                return AccessFlags.Write;

            return access;
        }

        internal static string BuildPortDisplayName(FieldInfo field, RenderGraphResource attr, AccessFlags access)
        {
            var displayName = RenderGraphPassReflectionUtility.GetRenderGraphResourceName(field, attr);
            var accessLabel = AccessFlagsToShortName(access);
            var attachmentLabel = attr.IsDepthAttachment
                ? "Depth"
                : attr.AttachmentIndex >= 0
                    ? $"A{attr.AttachmentIndex}"
                    : null;

            if (!string.IsNullOrEmpty(attachmentLabel))
                return $"{displayName} ({accessLabel}, {attachmentLabel})";

            return $"{displayName} ({accessLabel})";
        }

        internal static T ResolveConnectedNode<T>(AccessFlags access, T inputNode, T outputNode)
            where T : class
        {
            var canRead = CanRead(access);
            var canWrite = CanWrite(access);

            if (canRead && canWrite)
            {
                if (inputNode != null && outputNode != null && !ReferenceEquals(inputNode, outputNode))
                    return null;

                return inputNode ?? outputNode;
            }

            if (canRead)
                return inputNode;

            if (canWrite)
                return outputNode;

            return null;
        }

        private static string AccessFlagsToShortName(AccessFlags access)
        {
            var canRead = CanRead(access);
            var canWrite = CanWrite(access);

            if (canRead && canWrite)
                return "RW";

            if (canRead)
                return "R";

            if (canWrite)
                return "W";

            return access.ToString();
        }
    }
}
