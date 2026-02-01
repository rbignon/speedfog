// writer/SpeedFogWriter/Helpers/FogAssetHelper.cs
using SoulsFormats;
using System.Numerics;

namespace SpeedFogWriter.Helpers;

public class FogAssetHelper
{
    private readonly Dictionary<string, MSBE> _msbs;
    private MSBE.Part.Asset? _templateAsset;
    private int _nextPartIndex = 20000;

    public static readonly HashSet<string> FogWallModels = new()
    {
        "AEG099_230", "AEG099_231", "AEG099_232"
    };

    public FogAssetHelper(Dictionary<string, MSBE> msbs)
    {
        _msbs = msbs;
        InitializeTemplate();
    }

    private void InitializeTemplate()
    {
        const string templateMap = "m60_46_38_00";
        const string templateAssetName = "AEG007_310_2000";

        if (_msbs.TryGetValue(templateMap, out var msb))
        {
            _templateAsset = msb.Parts.Assets.Find(a => a.Name == templateAssetName);
        }

        if (_templateAsset == null)
        {
            Console.WriteLine($"  Warning: Template asset {templateAssetName} not found in {templateMap}");
        }
    }

    public MSBE.Part.Asset? CreateFogGate(
        string mapId,
        string modelName,
        Vector3 position,
        Vector3 rotation,
        uint entityId,
        bool enableSfx = true)
    {
        if (!_msbs.TryGetValue(mapId, out var msb))
        {
            Console.WriteLine($"  Warning: MSB not loaded for map {mapId}");
            return null;
        }

        if (_templateAsset == null)
        {
            Console.WriteLine($"  Warning: Template asset not initialized, cannot create fog gate");
            return null;
        }

        var newAsset = (MSBE.Part.Asset)_templateAsset.DeepCopy();
        newAsset.ModelName = modelName;

        AddAssetModel(msb, modelName);

        var assetName = $"{modelName}_{_nextPartIndex++}";
        SetAssetName(newAsset, assetName);

        newAsset.Position = position;
        newAsset.Rotation = rotation;
        newAsset.EntityID = entityId;

        if (FogWallModels.Contains(modelName))
        {
            newAsset.AssetSfxParamRelativeID = enableSfx ? (short)0 : (short)-1;
        }

        msb.Parts.Assets.Add(newAsset);
        return newAsset;
    }

    private static void AddAssetModel(MSBE msb, string modelName)
    {
        if (msb.Models.Assets.Any(m => m.Name == modelName))
            return;

        msb.Models.Assets.Add(new MSBE.Model.Asset
        {
            Name = modelName,
            SibPath = $@"N:\GR\data\Asset\Environment\geometry\{modelName[..6]}\{modelName}\sib\{modelName}.sib"
        });
    }

    private static void SetAssetName(MSBE.Part.Asset asset, string newName)
    {
        var oldName = asset.Name;
        asset.Name = newName;

        if (int.TryParse(newName.Split('_').Last(), out var unk))
        {
            asset.Unk08 = unk;
        }

        for (int i = 0; i < asset.UnkPartNames.Length; i++)
        {
            if (asset.UnkPartNames[i] == oldName)
            {
                asset.UnkPartNames[i] = newName;
            }
        }

        if (asset.UnkT54PartName == oldName)
        {
            asset.UnkT54PartName = newName;
        }
    }
}
