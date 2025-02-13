using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;
using Memoria;
using Memoria.Data;
using Memoria.Prime;
using Object = System.Object;
using System.Net.Mime;
using Assets.Sources.Scripts.UI.Common;

public static class AssetManager
{
	/*
	New system for Memoria:
	 1) Assets can be read from mod folders (defined in Memoria.Configuration.Mod)
	 - They can be either read from bundle archives or as plain non-archived files
	 - Assets in non-bundle archives (typically "resources.assets", but also all the "levelX" and "sharedassetsX.assets") can currently only be read as plain files when they are in a mod's folder
	 - They are read in priority from the first mod's folder up to the last mod's folder, then from the default folder
	 - Bundle archives should be placed directly in the mod's folder
	 - Plain files should be placed in a subfolder of the mod's folder respecting the asset's access path (eg. "EmbeddedAsset/Manifest/Text/Localization.txt")
	 - Only assets of certain types can be read as plain files currently: binary (TextAsset), text (TextAsset) and textures (Texture / Texture2D)
	 - Texture files that are not in archives should be placed as PNG or JPG files, not DXT-compressed files or using Unity's format
	 - Assets that are not present in the mod folders must be archived as normally in the default folder
	 2) Any asset that is not archived can use a "Memoria information" file which is a text file placed in the same subfolder as the asset, with the same name and the extension "AssetManager.MemoriaInfoExtension" (".memnfo"); the purpose of these files may vary from type to type
	 - For textures, they can define different properties of the texture that are usually in the Unity-formatted assets, such as the anisotropic level (see "AssetManager.ApplyTextureGenericMemoriaInfo")
	 - For sounds and musics, they can define or change different properties, such as the LoopStart/LoopEnd frame points (see "SoundLoaderResources.Load")
	 - For battle scene binary files (".raw16"), they can give battle properties as textual informations, which can be useful for adding custom properties to Memoria or go beyond binary max limits (see "BTL_SCENE.ReadBattleScene")
	 - Etc... [To be completed]
	 - The idea behind these files is that they should be optional (non-modded assets don't need them) and allow to use asset-specific special features without forcing other mods to take these features into account (for explicitely disabling them for instance)
	*/

	static AssetManager()
	{
		Array values = Enum.GetValues(typeof(AssetManagerUtil.ModuleBundle));
		String[] foldname = new String[Configuration.Mod.FolderNames.Length+1];
		String url;
		for (Int32 i = 0; i < (Int32)Configuration.Mod.FolderNames.Length; ++i)
			foldname[i] = Configuration.Mod.FolderNames[i] + "/";
		foldname[Configuration.Mod.FolderNames.Length] = "";
		Folder = new AssetFolder[foldname.Length];
		for (Int32 i = 0; i < (Int32)foldname.Length; ++i)
		{
			Folder[i] = new AssetFolder(foldname[i]);
			foreach (Object obj in values)
			{
				AssetManagerUtil.ModuleBundle moduleBundle = (AssetManagerUtil.ModuleBundle)((Int32)obj);
				if (moduleBundle == AssetManagerUtil.ModuleBundle.FieldMaps)
				{
					Array values2 = Enum.GetValues(typeof(AssetManagerUtil.FieldMapBundleId));
					foreach (Object obj2 in values2)
					{
						AssetManagerUtil.FieldMapBundleId bundleId = (AssetManagerUtil.FieldMapBundleId)((Int32)obj2);
						url = foldname[i] + AssetManagerUtil.GetStreamingAssetsPath() + "/" + AssetManagerUtil.CreateFieldMapBundleFilename(Application.platform, false, bundleId);
						if (File.Exists(url))
							Folder[i].DictAssetBundleRefs.Add(AssetManagerUtil.GetFieldMapBundleName(bundleId), new AssetBundleRef(url, 0));
					}
				}
				else if (moduleBundle == AssetManagerUtil.ModuleBundle.Sounds)
				{
					Array values3 = Enum.GetValues(typeof(AssetManagerUtil.SoundBundleId));
					foreach (Object obj3 in values3)
					{
						AssetManagerUtil.SoundBundleId bundleId2 = (AssetManagerUtil.SoundBundleId)((Int32)obj3);
						url = foldname[i] + AssetManagerUtil.GetStreamingAssetsPath() + "/" + AssetManagerUtil.CreateSoundBundleFilename(Application.platform, false, bundleId2);
						if (File.Exists(url))
							Folder[i].DictAssetBundleRefs.Add(AssetManagerUtil.GetSoundBundleName(bundleId2), new AssetBundleRef(url, 0));
					}
				}
				else
				{
					url = foldname[i] + AssetManagerUtil.GetStreamingAssetsPath() + "/" + AssetManagerUtil.CreateModuleBundleFilename(Application.platform, false, moduleBundle);
					if (File.Exists(url))
						Folder[i].DictAssetBundleRefs.Add(AssetManagerUtil.GetModuleBundleName(moduleBundle), new AssetBundleRef(url, 0));
				}
			}
		}
		_LoadAnimationFolderMapping();
	}

	private static void _LoadAnimationFolderMapping()
	{
		_animationInFolder = new Dictionary<String, List<String>>();
		String filestr = LoadString("EmbeddedAsset/Manifest/Animations/AnimationFolderMapping.txt", out _, false);
		if (filestr == null)
		{
			return;
		}
		String[] array = filestr.Split(new Char[]
		{
			'\n'
		});
		String[] array2 = array;
		for (Int32 i = 0; i < (Int32)array2.Length; i++)
		{
			String text = array2[i];
			String[] array3 = text.Split(new Char[]
			{
				':'
			});
			String text2 = array3[0];
			String[] array4 = array3[1].Split(new Char[]
			{
				','
			});
			List<String> list = new List<String>();
			String[] array5 = array4;
			for (Int32 j = 0; j < (Int32)array5.Length; j++)
			{
				String str = array5[j];
				String text3 = text2 + "/" + str;
				text3 = text3.Trim();
				list.Add(text3);
			}
			_animationInFolder.Add(text2, list);
		}
	}

	public static void ClearCache()
	{
		Caching.CleanCache();
		foreach (AssetFolder modfold in Folder)
			foreach (KeyValuePair<String, AssetBundleRef> keyValuePair in modfold.DictAssetBundleRefs)
			{
				AssetBundleRef value = keyValuePair.Value;
				if (value.assetBundle != (UnityEngine.Object)null)
				{
					value.assetBundle.Unload(true);
					value.assetBundle = (AssetBundle)null;
				}
			}
	}

    /*
	public static Byte[] LoadBinary(String resourcePath)
    {
		// By default, this method is only used for Localization.txt for a subsequent ByteReader.ReadCSV call
		// We delete it and use LoadBytes instead
        TextAsset textAsset = Resources.Load<TextAsset>(resourcePath);
        if (textAsset == null)
            throw new FileNotFoundException(resourcePath);

        return textAsset.bytes;
    }
	*/
	
	public static void ApplyTextureGenericMemoriaInfo<T>(ref T texture, ref String[] memoriaInfo) where T : UnityEngine.Texture
	{
		// Maybe remove the successfully parsed lines from the "info" array?
		foreach (String s in memoriaInfo)
        {
			String[] textureCode = s.Split(' ');
			if (textureCode.Length >= 2 && String.Compare(textureCode[0], "AnisotropicLevel") == 0)
			{
				Int32 anisoLevel;
				if (Int32.TryParse(textureCode[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out anisoLevel) && anisoLevel >= 1 && anisoLevel <= 9)
					texture.anisoLevel = anisoLevel;
			} else if (textureCode.Length >= 2 && String.Compare(textureCode[0], "FilterMode") == 0)
			{
				foreach (FilterMode m in (FilterMode[]) Enum.GetValues(typeof(FilterMode)))
					if (String.Compare(textureCode[1], m.ToString()) == 0)
						texture.filterMode = m;
			}
			else if(textureCode.Length >= 2 && String.Compare(textureCode[0], "HideFlags") == 0)
			{
				foreach (HideFlags f in (HideFlags[]) Enum.GetValues(typeof(HideFlags)))
					if (String.Compare(textureCode[1], f.ToString()) == 0)
						texture.hideFlags = f;
			}
			else if(textureCode.Length >= 2 && String.Compare(textureCode[0], "MipMapBias") == 0)
			{
				Single mipMapBias;
				if (Single.TryParse(textureCode[1], out mipMapBias))
					texture.mipMapBias = mipMapBias;
			}
			else if(textureCode.Length >= 2 && String.Compare(textureCode[0], "WrapMode") == 0)
			{
				foreach (TextureWrapMode m in (TextureWrapMode[]) Enum.GetValues(typeof(TextureWrapMode)))
					if (String.Compare(textureCode[1], m.ToString()) == 0)
						texture.wrapMode = m;
			}
		}
	}

	public static Boolean IsTextureFormatReadableFromBytes(TextureFormat format)
	{
		// Todo: verify which TextureFormat are compatible with LoadRawTextureData
		return format == TextureFormat.Alpha8
			|| format == TextureFormat.RGB24
			|| format == TextureFormat.RGBA32
			|| format == TextureFormat.ARGB32
			|| format == TextureFormat.RGB565
			|| format == TextureFormat.DXT1
			|| format == TextureFormat.DXT5;
	}

	public static Texture2D LoadTextureGeneric(Byte[] raw)
	{
		const UInt32 DDS_HEADER_SIZE = 0x3C;
		if (raw.Length >= DDS_HEADER_SIZE)
		{
			UInt32 w = BitConverter.ToUInt32(raw, 0);
			UInt32 h = BitConverter.ToUInt32(raw, 4);
			UInt32 imgSize = BitConverter.ToUInt32(raw, 8);
			TextureFormat fmt = (TextureFormat)BitConverter.ToUInt32(raw, 12);
			UInt32 mipCount = BitConverter.ToUInt32(raw, 16);
			// UInt32 flags = BitConverter.ToUInt32(raw, 20); // 1 = isReadable (for use of GetPixels); 0x100 is usually on
			// UInt32 imageCount = BitConverter.ToUInt32(raw, 24);
			// UInt32 dimension = BitConverter.ToUInt32(raw, 28);
			// UInt32 filterMode = BitConverter.ToUInt32(raw, 32);
			// UInt32 anisotropic = BitConverter.ToUInt32(raw, 36);
			// UInt32 mipBias = BitConverter.ToUInt32(raw, 40);
			// UInt32 wrapMode = BitConverter.ToUInt32(raw, 44);
			// UInt32 lightmapFormat = BitConverter.ToUInt32(raw, 48);
			// UInt32 colorSpace = BitConverter.ToUInt32(raw, 52);
			// UInt32 imgSize = BitConverter.ToUInt32(raw, 56);
			if (raw.Length == DDS_HEADER_SIZE + imgSize && IsTextureFormatReadableFromBytes(fmt))
			{
				Byte[] imgBytes = new Byte[imgSize];
				Buffer.BlockCopy(raw, (Int32)DDS_HEADER_SIZE, imgBytes, 0, (Int32)imgSize);
				Texture2D ddsTexture = new Texture2D((Int32)w, (Int32)h, fmt, mipCount > 1);
				ddsTexture.LoadRawTextureData(imgBytes);
				ddsTexture.Apply();
				return ddsTexture;
			}
		}
		Texture2D pngTexture = new Texture2D(1, 1, DefaultTextureFormat, false);
		if (pngTexture.LoadImage(raw))
			return pngTexture;
		return null;
	}

	public static T LoadFromDisc<T>(String name, ref String[] memoriaInfo, String archiveName)
	{
		/*
		Types used by the game by default:
		 * TextAsset (many, both text and binaries, including sounds and musics) - Use LoadString / LoadBytes instead
		 * Texture2D (LoadAsync is used in MBG) - Can be read as PNG/JPG
		 * Texture (only used by ModelFactory for "GEO_MAIN_F3_ZDN", "GEO_MAIN_F4_ZDN" and "GEO_MAIN_F5_ZDN") - Can be read as PNG/JPG as Texture2D
		 * RenderTexture (Usually split into many pieces) - Can't be read from disc currently
		 * Material - Can't be read from disc currently
		 * AnimationClip (LoadAll is used in AnimationFactory) - Can't be read from disc currently
		 * GameObject (Usually split into many pieces ; LoadAsync is used in WMWorld) - Can't be read from disc currently
		*/
		if (typeof(T) == typeof(String))
		{
			return (T)(Object)File.ReadAllText(name);
		}
		else if (typeof(T) == typeof(Byte[]))
		{
			return (T)(Object)File.ReadAllBytes(name);
		}
		else if (typeof(T) == typeof(Texture2D) || typeof(T) == typeof(Texture))
		{
			Byte[] raw = File.ReadAllBytes(name);
			Texture2D newTexture = LoadTextureGeneric(raw);
			if (newTexture == null)
				newTexture = new Texture2D(1, 1, DefaultTextureFormat, false);
			ApplyTextureGenericMemoriaInfo<Texture2D>(ref newTexture, ref memoriaInfo);
			return (T)(Object)newTexture;
		}
		else if (typeof(T) == typeof(Sprite))
		{
			Byte[] raw = File.ReadAllBytes(name);
			Texture2D newTexture = LoadTextureGeneric(raw);
			if (newTexture == null)
				newTexture = new Texture2D(1, 1, DefaultTextureFormat, false);
			ApplyTextureGenericMemoriaInfo<Texture2D>(ref newTexture, ref memoriaInfo);
			Sprite newSprite = Sprite.Create(newTexture, new Rect(0, 0, newTexture.width, newTexture.height), new Vector2(0.5f, 0.5f));
			return (T)(Object)newSprite;
		}
		else if (typeof(T) == typeof(UIAtlas))
		{
			// Todo: Maybe avoid the call of Resources.Load<UIAtlas> if a complete .tpsheet is available and it's not necessary to get the original's sprite list
			UIAtlas newAtlas = Resources.Load<UIAtlas>(archiveName);
			if (newAtlas != null)
				newAtlas.ReadFromDisc(name);
			else
				Log.Message("[AssetManager] Embeded asset not found: " + archiveName);
			return (T)(Object)newAtlas;
		}
		Log.Message("[AssetManager] Trying to load from disc the asset " + name + " of type " + typeof(T).ToString() + ", which is not currently possible");
		return (T)(Object)null;
	}

	public static T Load<T>(String name, out String[] info, Boolean suppressError = false) where T : UnityEngine.Object
	{
		String infoFileName = Path.ChangeExtension(name, MemoriaInfoExtension);
		info = new String[0];
		T result;
		if (AssetManagerForObb.IsUseOBB)
			return AssetManagerForObb.Load<T>(name, suppressError);
		if (!UseBundles || AssetManagerUtil.IsEmbededAssets(name))
		{
			foreach (AssetFolder modfold in Folder)
				if (File.Exists(modfold.FolderPath + AssetManagerUtil.GetResourcesAssetsPath(true) + "/" + name))
				{
					if (File.Exists(modfold.FolderPath + AssetManagerUtil.GetResourcesAssetsPath(true) + "/" + infoFileName))
						info = File.ReadAllLines(modfold.FolderPath + AssetManagerUtil.GetResourcesAssetsPath(true) + "/" + infoFileName);
					return LoadFromDisc<T>(modfold.FolderPath + AssetManagerUtil.GetResourcesAssetsPath(true) + "/" + name, ref info, name);
				}
			result = Resources.Load<T>(name);
			if (result == (UnityEngine.Object)null)
				Log.Message("[AssetManager] Embeded asset not found: " + name);
			return result;
		}
		String belongingBundleFilename = AssetManagerUtil.GetBelongingBundleFilename(name);
		if (!String.IsNullOrEmpty(belongingBundleFilename))
		{
			String nameInBundle = AssetManagerUtil.GetResourcesBasePath() + name + AssetManagerUtil.GetAssetExtension<T>(name);
			foreach (AssetFolder modfold in Folder)
			{
				if (File.Exists(modfold.FolderPath + AssetManagerUtil.GetStreamingAssetsPath() + "/" + nameInBundle))
				{
					if (File.Exists(modfold.FolderPath + AssetManagerUtil.GetStreamingAssetsPath() + "/" + AssetManagerUtil.GetResourcesBasePath() + infoFileName))
						info = File.ReadAllLines(modfold.FolderPath + AssetManagerUtil.GetStreamingAssetsPath() + "/" + AssetManagerUtil.GetResourcesBasePath() + infoFileName);
					return LoadFromDisc<T>(modfold.FolderPath + AssetManagerUtil.GetStreamingAssetsPath() + "/" + nameInBundle, ref info, nameInBundle);
				}
				AssetBundleRef assetBundleRef = null;
				modfold.DictAssetBundleRefs.TryGetValue(belongingBundleFilename, out assetBundleRef);
				if (assetBundleRef != null && assetBundleRef.assetBundle != (UnityEngine.Object)null)
				{
					result = assetBundleRef.assetBundle.LoadAsset<T>(nameInBundle);
					if (result != (UnityEngine.Object)null)
						return result;
				}
			}
		}
		if (ForceUseBundles)
			return (T)((Object)null);
		foreach (AssetFolder modfold in Folder)
			if (File.Exists(modfold.FolderPath + AssetManagerUtil.GetResourcesAssetsPath(true) + "/" + name))
			{
				if (File.Exists(modfold.FolderPath + AssetManagerUtil.GetResourcesAssetsPath(true) + "/" + infoFileName))
					info = File.ReadAllLines(modfold.FolderPath + AssetManagerUtil.GetResourcesAssetsPath(true) + "/" + infoFileName);
				return LoadFromDisc<T>(modfold.FolderPath + AssetManagerUtil.GetResourcesAssetsPath(true) + "/" + name, ref info, name);
			}
		result = Resources.Load<T>(name);
		if (result == (UnityEngine.Object)null)
			Log.Message("[AssetManager] Asset not found: " + name);
		return result;
	}
	
	public static String LoadString(String name, out String[] info, Boolean suppressError = false)
	{
		String infoFileName = Path.ChangeExtension(name, MemoriaInfoExtension);
		TextAsset txt = null;
		info = new String[0];
		if (AssetManagerForObb.IsUseOBB)
		{
			txt = AssetManagerForObb.Load<TextAsset>(name, suppressError);
			if (txt != (UnityEngine.Object)null)
				return txt.text;
			return null;
		}
		if (!UseBundles || AssetManagerUtil.IsEmbededAssets(name))
		{
			foreach (AssetFolder modfold in Folder)
				if (File.Exists(modfold.FolderPath + AssetManagerUtil.GetResourcesAssetsPath(true) + "/" + name))
				{
					if (File.Exists(modfold.FolderPath + AssetManagerUtil.GetResourcesAssetsPath(true) + "/" + infoFileName))
						info = File.ReadAllLines(modfold.FolderPath + AssetManagerUtil.GetResourcesAssetsPath(true) + "/" + infoFileName);
					return LoadFromDisc<String>(modfold.FolderPath + AssetManagerUtil.GetResourcesAssetsPath(true) + "/" + name, ref info, name);
				}
			txt = Resources.Load<TextAsset>(name);
			if (txt != (UnityEngine.Object)null)
				return txt.text;
			Log.Message("[AssetManager] Embeded asset not found: " + name);
			return null;
		}
		String belongingBundleFilename = AssetManagerUtil.GetBelongingBundleFilename(name);
		if (!String.IsNullOrEmpty(belongingBundleFilename))
		{
			String nameInBundle = AssetManagerUtil.GetResourcesBasePath() + name + AssetManagerUtil.GetAssetExtension<TextAsset>(name);
			foreach (AssetFolder modfold in Folder)
			{
				if (File.Exists(modfold.FolderPath + AssetManagerUtil.GetStreamingAssetsPath() + "/" + nameInBundle))
				{
					if (File.Exists(modfold.FolderPath + AssetManagerUtil.GetStreamingAssetsPath() + "/" + AssetManagerUtil.GetResourcesBasePath() + infoFileName))
						info = File.ReadAllLines(modfold.FolderPath + AssetManagerUtil.GetStreamingAssetsPath() + "/" + AssetManagerUtil.GetResourcesBasePath() + infoFileName);
					return LoadFromDisc<String>(modfold.FolderPath + AssetManagerUtil.GetStreamingAssetsPath() + "/" + nameInBundle, ref info, nameInBundle);
				}
				AssetBundleRef assetBundleRef = null;
				modfold.DictAssetBundleRefs.TryGetValue(belongingBundleFilename, out assetBundleRef);
				if (assetBundleRef != null && assetBundleRef.assetBundle != (UnityEngine.Object)null)
				{
					txt = assetBundleRef.assetBundle.LoadAsset<TextAsset>(nameInBundle);
					if (txt != (UnityEngine.Object)null)
						return txt.text;
				}
			}
		}
		if (ForceUseBundles)
			return null;
		foreach (AssetFolder modfold in Folder)
			if (File.Exists(modfold.FolderPath + AssetManagerUtil.GetResourcesAssetsPath(true) + "/" + name))
			{
				if (File.Exists(modfold.FolderPath + AssetManagerUtil.GetResourcesAssetsPath(true) + "/" + infoFileName))
					info = File.ReadAllLines(modfold.FolderPath + AssetManagerUtil.GetResourcesAssetsPath(true) + "/" + infoFileName);
				return LoadFromDisc<String>(modfold.FolderPath + AssetManagerUtil.GetResourcesAssetsPath(true) + "/" + name, ref info, name);
			}
		txt = Resources.Load<TextAsset>(name);
		if (txt != (UnityEngine.Object)null)
			return txt.text;
		Log.Message("[AssetManager] Asset not found: " + name);
		return null;
	}
	
	public static Byte[] LoadBytes(String name, out String[] info, Boolean suppressError = false)
	{
		String infoFileName = Path.ChangeExtension(name, MemoriaInfoExtension);
		TextAsset txt = null;
		info = new String[0];
		if (AssetManagerForObb.IsUseOBB)
		{
			txt = AssetManagerForObb.Load<TextAsset>(name, suppressError);
			if (txt != (UnityEngine.Object)null)
				return txt.bytes;
			return null;
		}
		if (!UseBundles || AssetManagerUtil.IsEmbededAssets(name))
		{
			foreach (AssetFolder modfold in Folder)
				if (File.Exists(modfold.FolderPath + AssetManagerUtil.GetResourcesAssetsPath(true) + "/" + name))
				{
					if (File.Exists(modfold.FolderPath + AssetManagerUtil.GetResourcesAssetsPath(true) + "/" + infoFileName))
						info = File.ReadAllLines(modfold.FolderPath + AssetManagerUtil.GetResourcesAssetsPath(true) + "/" + infoFileName);
					return LoadFromDisc<Byte[]>(modfold.FolderPath + AssetManagerUtil.GetResourcesAssetsPath(true) + "/" + name, ref info, name);
				}
			txt = Resources.Load<TextAsset>(name);
			if (txt != (UnityEngine.Object)null)
				return txt.bytes;
			Log.Message("[AssetManager] Embeded asset not found: " + name);
			return null;
		}
		String belongingBundleFilename = AssetManagerUtil.GetBelongingBundleFilename(name);
		if (!String.IsNullOrEmpty(belongingBundleFilename))
		{
			String nameInBundle = AssetManagerUtil.GetResourcesBasePath() + name + AssetManagerUtil.GetAssetExtension<TextAsset>(name);
			foreach (AssetFolder modfold in Folder)
			{
				if (File.Exists(modfold.FolderPath + AssetManagerUtil.GetStreamingAssetsPath() + "/" + nameInBundle))
				{
					if (File.Exists(modfold.FolderPath + AssetManagerUtil.GetStreamingAssetsPath() + "/" + AssetManagerUtil.GetResourcesBasePath() + infoFileName))
						info = File.ReadAllLines(modfold.FolderPath + AssetManagerUtil.GetStreamingAssetsPath() + "/" + AssetManagerUtil.GetResourcesBasePath() + infoFileName);
					return LoadFromDisc<Byte[]>(modfold.FolderPath + AssetManagerUtil.GetStreamingAssetsPath() + "/" + nameInBundle, ref info, nameInBundle);
				}
				AssetBundleRef assetBundleRef = null;
				modfold.DictAssetBundleRefs.TryGetValue(belongingBundleFilename, out assetBundleRef);
				if (assetBundleRef != null && assetBundleRef.assetBundle != (UnityEngine.Object)null)
				{
					txt = assetBundleRef.assetBundle.LoadAsset<TextAsset>(nameInBundle);
					if (txt != (UnityEngine.Object)null)
						return txt.bytes;
				}
			}
		}
		if (ForceUseBundles)
			return null;
		foreach (AssetFolder modfold in Folder)
			if (File.Exists(modfold.FolderPath + AssetManagerUtil.GetResourcesAssetsPath(true) + "/" + name))
			{
				if (File.Exists(modfold.FolderPath + AssetManagerUtil.GetResourcesAssetsPath(true) + "/" + infoFileName))
					info = File.ReadAllLines(modfold.FolderPath + AssetManagerUtil.GetResourcesAssetsPath(true) + "/" + infoFileName);
				return LoadFromDisc<Byte[]>(modfold.FolderPath + AssetManagerUtil.GetResourcesAssetsPath(true) + "/" + name, ref info, name);
			}
		txt = Resources.Load<TextAsset>(name);
		if (txt != (UnityEngine.Object)null)
			return txt.bytes;
		Log.Message("[AssetManager] Asset not found: " + name);
		return null;
	}

	public static AssetManagerRequest LoadAsync<T>(String name) where T : UnityEngine.Object
	{
		if (AssetManagerForObb.IsUseOBB)
			return AssetManagerForObb.LoadAsync<T>(name);
		if (!UseBundles || AssetManagerUtil.IsEmbededAssets(name))
		{
			ResourceRequest resourceRequest = Resources.LoadAsync<T>(name);
			if (resourceRequest != null)
				return new AssetManagerRequest(resourceRequest, (AssetBundleRequest)null);
		}
		String belongingBundleFilename = AssetManagerUtil.GetBelongingBundleFilename(name);
		if (!String.IsNullOrEmpty(belongingBundleFilename))
		{
			String nameInBundle = AssetManagerUtil.GetResourcesBasePath() + name + AssetManagerUtil.GetAssetExtension<T>(name);
			foreach (AssetFolder modfold in Folder)
			{
				AssetBundleRef assetBundleRef = null;
				modfold.DictAssetBundleRefs.TryGetValue(belongingBundleFilename, out assetBundleRef);
				if (assetBundleRef != null && assetBundleRef.assetBundle != (UnityEngine.Object)null)
				{
					AssetBundleRequest assetBundleRequest = assetBundleRef.assetBundle.LoadAssetAsync(nameInBundle);
					if (assetBundleRequest != null)
						return new AssetManagerRequest((ResourceRequest)null, assetBundleRequest);
				}
			}
		}
		if (ForceUseBundles)
			return (AssetManagerRequest)null;
		ResourceRequest resourceRequest2 = Resources.LoadAsync<T>(name);
		if (resourceRequest2 != null)
			return new AssetManagerRequest(resourceRequest2, (AssetBundleRequest)null);
		Log.Message("[AssetManager] Asset not found: " + name);
		return (AssetManagerRequest)null;
	}

	public static T[] LoadAll<T>(String name) where T : UnityEngine.Object
	{
		if (AssetManagerForObb.IsUseOBB)
		{
			return AssetManagerForObb.LoadAll<T>(name);
		}
		if (typeof(T) != typeof(AnimationClip))
		{
			return null;
		}
		if (!UseBundles)
		{
			name = AnimationFactory.GetRenameAnimationDirectory(name);
			return Resources.LoadAll<T>(name);
		}
		if (_animationInFolder.ContainsKey(name))
		{
			List<String> list = _animationInFolder[name];
			T[] array = new T[list.Count];
			for (Int32 i = 0; i < list.Count; i++)
			{
				String renameAnimationPath = AnimationFactory.GetRenameAnimationPath(list[i]);
				array[i] = Load<T>(renameAnimationPath, out _, false);
			}
			return array;
		}
		return null;
	}

	public static void PatchDictionaries(String[] patchCode)
	{
		// This method might go somewhere else...
		foreach (String s in patchCode)
		{
			String[] entry = s.Split(' ');
			if (entry.Length < 3)
				continue;
			if (String.Compare(entry[0], "MessageFile") == 0)
            {
				// eg.: MessageFile 2000 MES_CUSTOM_PLACE
				if (FF9DBAll.MesDB == null)
					continue;
				Int32 ID;
				if (!Int32.TryParse(entry[1], out ID))
					continue;
				FF9DBAll.MesDB[ID] = entry[2];
			}
			else if(String.Compare(entry[0], "IconSprite") == 0)
			{
				// eg.: IconSprite 19 arrow_down
				if (FF9UIDataTool.IconSpriteName == null)
					continue;
				Int32 ID;
				if (!Int32.TryParse(entry[1], out ID))
					continue;
				FF9UIDataTool.IconSpriteName[ID] = entry[2];
			}
			else if (String.Compare(entry[0], "DebuffIcon") == 0)
			{
				// eg.: DebuffIcon 0 188
				// or : DebuffIcon 0 ability_stone
				if (BattleHUD.DebuffIconNames == null)
					continue;
				Int32 ID, iconID;
				if (!Int32.TryParse(entry[1], out ID))
					continue;
				if (Int32.TryParse(entry[2], out iconID))
				{
					if (!FF9UIDataTool.IconSpriteName.ContainsKey(iconID))
                    {
						Log.Message("[AssetManager.PatchDictionaries] Trying to use the invalid sprite index " + iconID + " for the icon of status " + ID);
						continue;
                    }
					BattleHUD.DebuffIconNames[(BattleStatus)(1 << ID)] = FF9UIDataTool.IconSpriteName[iconID];
					if (BattleResultUI.BadIconDict == null || FF9UIDataTool.status_id == null)
						continue;
					BattleResultUI.BadIconDict[(UInt32)(1 << ID)] = (Byte)iconID;
					if (ID < FF9UIDataTool.status_id.Length)
						FF9UIDataTool.status_id[ID] = iconID;
					// Todo: debuff icons in the main menus (status menu, items...) are UISprite components of CharacterDetailHUD and are enabled/disabled in FF9UIDataTool.DisplayCharacterDetail
					// Maybe add UISprite components at runtime? The width of the window may require adjustments then
					// By design (in FF9UIDataTool.DisplayCharacterDetail for instance), permanent debuffs must be the first ones of the list of statuses
				}
				else
				{
					BattleHUD.DebuffIconNames[(BattleStatus)(1 << ID)] = entry[2]; // When adding a debuff icon by sprite name, not all the dictionaries are updated
				}
			}
			else if (String.Compare(entry[0], "BuffIcon") == 0)
			{
				// eg.: BuffIcon 18 188
				// or : BuffIcon 18 ability_stone
				if (BattleHUD.BuffIconNames == null)
					continue;
				Int32 ID, iconID;
				if (!Int32.TryParse(entry[1], out ID))
					continue;
				if (Int32.TryParse(entry[2], out iconID))
				{
					if (!FF9UIDataTool.IconSpriteName.ContainsKey(iconID))
					{
						Log.Message("[AssetManager.PatchDictionaries] Trying to use the invalid sprite index " + iconID + " for the icon of status " + ID);
						continue;
					}
					BattleHUD.BuffIconNames[(BattleStatus)(1 << ID)] = FF9UIDataTool.IconSpriteName[iconID];
				}
				else
				{
					BattleHUD.BuffIconNames[(BattleStatus)(1 << ID)] = entry[2];
				}
			}
			else if (String.Compare(entry[0], "HalfTranceCommand") == 0)
			{
				// eg.: HalfTranceCommand Set DoubleWhiteMagic DoubleBlackMagic HolySword2
				if (btl_cmd.half_trance_cmd_list == null)
					continue;
				Boolean add = String.Compare(entry[1], "Remove") != 0;
				if (String.Compare(entry[1], "Set") == 0)
					btl_cmd.half_trance_cmd_list.Clear();
				for (Int32 i = 2; i< entry.Length; i++)
					foreach (BattleCommandId cmdid in (BattleCommandId[])Enum.GetValues(typeof(BattleCommandId)))
						if (String.Compare(entry[i], cmdid.ToString()) == 0)
						{
							if (add && !btl_cmd.half_trance_cmd_list.Contains(cmdid))
								btl_cmd.half_trance_cmd_list.Add(cmdid);
							else if (!add)
								btl_cmd.half_trance_cmd_list.Remove(cmdid);
							break;
						}
			}
			else if (String.Compare(entry[0], "BattleMapModel") == 0)
			{
				// eg.: BattleMapModel BSC_CUSTOM_FIELD BBG_B065
				// Can also be modified using "BattleScene"
				if (FF9BattleDB.MapModel == null)
					continue;
				FF9BattleDB.MapModel[entry[1]] = entry[2];
			}
			else if (String.Compare(entry[0], "FieldScene") == 0 && entry.Length >= 6)
			{
				// eg.: FieldScene 4000 57 CUSTOM_FIELD CUSTOM_FIELD 2000
				if (FF9DBAll.EventDB == null || EventEngineUtils.eventIDToFBGID == null || EventEngineUtils.eventIDToMESID == null)
					continue;
				Int32 ID, mesID, areaID;
				if (!Int32.TryParse(entry[1], out ID))
					continue;
				if (!Int32.TryParse(entry[2], out areaID))
					continue;
				if (!Int32.TryParse(entry[5], out mesID))
					continue;
				if (!FF9DBAll.MesDB.ContainsKey(mesID))
				{
					Log.Message("[AssetManager.PatchDictionaries] Trying to use the invalid message file ID " + mesID + " for the field map field scene " + entry[3] + " (" + ID + ")");
					continue;
				}
				String fieldMapName = "FBG_N" + areaID + "_" + entry[3];
				EventEngineUtils.eventIDToFBGID[ID] = fieldMapName;
				FF9DBAll.EventDB[ID] = "EVT_" + entry[4];
				EventEngineUtils.eventIDToMESID[ID] = mesID;
				// p0data1X:
				//  Assets/Resources/FieldMaps/{fieldMapName}/atlas.png
				//  Assets/Resources/FieldMaps/{fieldMapName}/{fieldMapName}.bgi.bytes
				//  Assets/Resources/FieldMaps/{fieldMapName}/{fieldMapName}.bgs.bytes
				//  [Optional] Assets/Resources/FieldMaps/{fieldMapName}/spt.tcb.bytes
				//  [Optional for each sps] Assets/Resources/FieldMaps/{fieldMapName}/{spsID}.sps.bytes
				// p0data7:
				//  Assets/Resources/CommonAsset/EventEngine/EventBinary/Field/LANG/EVT_{entry[4]}.eb.bytes
				//  [Optional] Assets/Resources/CommonAsset/EventEngine/EventAnimation/EVT_{entry[4]}.txt.bytes
				//  [Optional] Assets/Resources/CommonAsset/MapConfigData/EVT_{entry[4]}.bytes
				//  [Optional] Assets/Resources/CommonAsset/VibrationData/EVT_{entry[4]}.bytes
			}
			else if (String.Compare(entry[0], "BattleScene") == 0 && entry.Length >= 4)
			{
				// eg.: BattleScene 5000 CUSTOM_BATTLE BBG_B065
				if (FF9DBAll.EventDB == null || FF9BattleDB.SceneData == null || FF9BattleDB.MapModel == null)
					continue;
				Int32 ID;
				if (!Int32.TryParse(entry[1], out ID))
					continue;
				FF9DBAll.EventDB[ID] = "EVT_BATTLE_" + entry[2];
				FF9BattleDB.SceneData["BSC_" + entry[2]] = ID;
				FF9BattleDB.MapModel["BSC_" + entry[2]] = entry[3];
				// p0data2:
				//  Assets/Resources/BattleMap/BattleScene/EVT_BATTLE_{entry[2]}/{ID}.raw17.bytes
				//  Assets/Resources/BattleMap/BattleScene/EVT_BATTLE_{entry[2]}/dbfile0000.raw16.bytes
				// p0data7:
				//  Assets/Resources/CommonAsset/EventEngine/EventBinary/Battle/{Lang}/EVT_BATTLE_{entry[2]}.eb.bytes
				// resources:
				//  EmbeddedAsset/Text/{Lang}/Battle/{ID}.mes
			}
			// Todo: add dictionary moddability for 3D models and animations, at least for modifying existing entries
		}
	}

	public const String MemoriaInfoExtension = ".memnfo";

	public const String MemoriaDictionaryPatcherPath = "DictionaryPatch.txt";

	public const TextureFormat DefaultTextureFormat = TextureFormat.ARGB32;

	public static Boolean UseBundles;

	public static Boolean ForceUseBundles;

	public static AssetFolder[] Folder;

	private static Dictionary<String, List<String>> _animationInFolder;

	public class AssetBundleRef
	{
		public AssetBundleRef(String strUrl, Int32 intVersionIn)
		{
			this.fullUrl = strUrl;
			this.version = intVersionIn;
		}

		public AssetBundle assetBundle;

		public Int32 version;

		public String fullUrl;
	}
	
	public class AssetFolder
	{
		public AssetFolder(String path)
		{
			this.FolderPath = path;
			this.DictAssetBundleRefs = new Dictionary<String, AssetBundleRef>();
		}

		public String FolderPath;

		public Dictionary<String, AssetBundleRef> DictAssetBundleRefs;
	}
}
