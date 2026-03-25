using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace VividRP.Editor.GPUDriven
{
    internal static class MeshletRendererGameObjectMenu
    {
        private const string ConvertRecursiveMenuPath = "GameObject/VividRP/GPUDriven/Convert MeshRenderers To MeshletRenderers Recursively";

        [MenuItem(ConvertRecursiveMenuPath, false, 20)]
        private static void ConvertMeshRenderersRecursively(MenuCommand command)
        {
            GameObject[] roots = ResolveSelectedRoots(command);
            if (roots.Length == 0)
            {
                return;
            }

            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Convert MeshRenderers To MeshletRenderers Recursively");

            int convertedRendererCount = 0;
            int addedMeshletRendererCount = 0;
            int failedRendererCount = 0;
            int skippedRendererCount = 0;
            var warnings = new List<string>();

            try
            {
                for (int rootIndex = 0; rootIndex < roots.Length; rootIndex++)
                {
                    MeshletRendererRecursiveConversionResult result =
                        MeshletRendererEditorUtility.TakeOverAndRemoveSourceMeshRenderersRecursively(roots[rootIndex]);

                    convertedRendererCount += result.ConvertedRendererCount;
                    addedMeshletRendererCount += result.AddedMeshletRendererCount;
                    failedRendererCount += result.FailedRendererCount;
                    skippedRendererCount += result.SkippedRendererCount;

                    if (!string.IsNullOrEmpty(result.ErrorMessage))
                    {
                        warnings.Add(result.ErrorMessage);
                    }

                    warnings.AddRange(result.Warnings);
                }
            }
            finally
            {
                Undo.CollapseUndoOperations(undoGroup);
            }

            Selection.objects = roots;
            LogSummary(roots, convertedRendererCount, addedMeshletRendererCount, failedRendererCount, skippedRendererCount, warnings);
        }

        [MenuItem(ConvertRecursiveMenuPath, true)]
        private static bool ValidateConvertMeshRenderersRecursively(MenuCommand command)
        {
            GameObject[] roots = ResolveSelectedRoots(command);
            for (int rootIndex = 0; rootIndex < roots.Length; rootIndex++)
            {
                if (MeshletRendererEditorUtility.HasConvertibleMeshRendererInHierarchy(roots[rootIndex]))
                {
                    return true;
                }
            }

            return false;
        }

        private static GameObject[] ResolveSelectedRoots(MenuCommand command)
        {
            var candidates = new List<GameObject>();
            if (command.context is GameObject contextObject)
            {
                candidates.Add(contextObject);
            }

            GameObject[] selectedObjects = Selection.gameObjects;
            for (int index = 0; index < selectedObjects.Length; index++)
            {
                GameObject selectedObject = selectedObjects[index];
                if (selectedObject != null && !candidates.Contains(selectedObject))
                {
                    candidates.Add(selectedObject);
                }
            }

            if (candidates.Count == 0 && Selection.activeGameObject != null)
            {
                candidates.Add(Selection.activeGameObject);
            }

            if (candidates.Count <= 1)
            {
                return candidates.ToArray();
            }

            var candidateSet = new HashSet<GameObject>(candidates);
            var roots = new List<GameObject>(candidates.Count);
            for (int index = 0; index < candidates.Count; index++)
            {
                GameObject candidate = candidates[index];
                if (candidate == null || HasSelectedAncestor(candidate.transform, candidateSet))
                {
                    continue;
                }

                roots.Add(candidate);
            }

            return roots.ToArray();
        }

        private static bool HasSelectedAncestor(Transform transform, HashSet<GameObject> candidates)
        {
            Transform parent = transform != null ? transform.parent : null;
            while (parent != null)
            {
                if (candidates.Contains(parent.gameObject))
                {
                    return true;
                }

                parent = parent.parent;
            }

            return false;
        }

        private static void LogSummary(
            GameObject[] roots,
            int convertedRendererCount,
            int addedMeshletRendererCount,
            int failedRendererCount,
            int skippedRendererCount,
            List<string> warnings)
        {
            var messageBuilder = new StringBuilder();
            messageBuilder.Append("[VividRP] Recursively converted ");
            messageBuilder.Append(convertedRendererCount);
            messageBuilder.Append(" MeshRenderer(s) to MeshletRenderer(s) across ");
            messageBuilder.Append(roots.Length);
            messageBuilder.Append(" root object(s).");

            if (addedMeshletRendererCount > 0 || failedRendererCount > 0 || skippedRendererCount > 0)
            {
                messageBuilder.Append(" Added ");
                messageBuilder.Append(addedMeshletRendererCount);
                messageBuilder.Append(", skipped ");
                messageBuilder.Append(skippedRendererCount);
                messageBuilder.Append(", failed ");
                messageBuilder.Append(failedRendererCount);
                messageBuilder.Append('.');
            }

            if (warnings != null && warnings.Count > 0)
            {
                messageBuilder.AppendLine();
                messageBuilder.AppendLine("Warnings:");
                for (int warningIndex = 0; warningIndex < warnings.Count; warningIndex++)
                {
                    messageBuilder.Append("- ");
                    messageBuilder.AppendLine(warnings[warningIndex]);
                }
            }

            if (failedRendererCount > 0 || (warnings != null && warnings.Count > 0))
            {
                Debug.LogWarning(messageBuilder.ToString(), roots.Length > 0 ? roots[0] : null);
                return;
            }

            Debug.Log(messageBuilder.ToString(), roots.Length > 0 ? roots[0] : null);
        }
    }
}
