using System;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace LWGUI
{
	internal static class LwguiAssetPathUtility
	{
		private const string LwguiScriptAssetPathSuffix = "/Editor/LWGUI.cs";
		private const string LwguiAsmdefAssetPathSuffix = "/Editor/LWGUI.asmdef";

		private static string _cachedRootPath;

		public static T LoadAssetAtRelativePath<T>(string relativePath) where T : Object
		{
			var assetPath = GetAssetPath(relativePath);
			return string.IsNullOrEmpty(assetPath) ? null : AssetDatabase.LoadAssetAtPath<T>(assetPath);
		}

		public static string GetAssetPath(string relativePath)
		{
			if (string.IsNullOrEmpty(relativePath))
			{
				return string.Empty;
			}

			var rootPath = GetRootPath();
			if (string.IsNullOrEmpty(rootPath))
			{
				return string.Empty;
			}

			return $"{rootPath}/{relativePath.Replace('\\', '/')}";
		}

		public static string GetAbsolutePath(string relativePath)
		{
			var assetPath = GetAssetPath(relativePath);
			return string.IsNullOrEmpty(assetPath) ? string.Empty : IOHelper.GetAbsPath(assetPath);
		}

		private static string GetRootPath()
		{
			if (!string.IsNullOrEmpty(_cachedRootPath))
			{
				return _cachedRootPath;
			}

			_cachedRootPath = FindRootPath(LwguiScriptAssetPathSuffix, "LWGUI t:script");
			if (!string.IsNullOrEmpty(_cachedRootPath))
			{
				return _cachedRootPath;
			}

			_cachedRootPath = FindRootPath(LwguiAsmdefAssetPathSuffix, "LWGUI t:asmdef");
			if (!string.IsNullOrEmpty(_cachedRootPath))
			{
				return _cachedRootPath;
			}

			Debug.LogError("LWGUI: Could not locate the LWGUI package root.");
			return string.Empty;
		}

		private static string FindRootPath(string assetPathSuffix, string filter)
		{
			foreach (var guid in AssetDatabase.FindAssets(filter))
			{
				var assetPath = AssetDatabase.GUIDToAssetPath(guid);
				if (assetPath.EndsWith(assetPathSuffix, StringComparison.Ordinal))
				{
					return assetPath.Substring(0, assetPath.Length - assetPathSuffix.Length);
				}
			}

			return string.Empty;
		}
	}
}
