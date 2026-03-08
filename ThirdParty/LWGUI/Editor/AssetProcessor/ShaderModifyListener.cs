// Copyright (c) Jason Ma
using System;
using System.IO;
using LWGUI.PerformanceMonitor;
using UnityEditor;
using UnityEngine;

namespace LWGUI
{
	/// <summary>
	/// Used to listen for Shader updates and flush the LWGUI caches
	/// </summary>
	public class ShaderModifyListener : AssetPostprocessor
	{
		private static void OnPostprocessAllAssets(string[] importedAssets, string[] deletedAssets, string[] movedAssets, string[] movedFromAssetPaths)
		{
			foreach (var assetPath in importedAssets)
			{
				var ext = Path.GetExtension(assetPath);
				if (ext.Equals(".shader", StringComparison.OrdinalIgnoreCase)
					|| ext.Equals(".shadergraph", StringComparison.OrdinalIgnoreCase)
					)
				{
					var shader = AssetDatabase.LoadAssetAtPath<Shader>(assetPath);
					if (shader == null) continue;
					
					MetaDataHelper.ReleaseShaderMetadataCache(shader);
					ShaderPerfMonitor.ClearShaderPerfCache(shader);
					ReflectionHelper.InvalidatePropertyCache(shader);
				}
			}
		}
	}
}