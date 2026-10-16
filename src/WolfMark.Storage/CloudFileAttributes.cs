namespace WolfMark.Storage;

/// <summary>OneDrive Files On-Demand placeholder detection (SPEC §4.1).</summary>
public static class CloudFileAttributes
{
    /// <summary>FILE_ATTRIBUTE_RECALL_ON_OPEN.</summary>
    public const FileAttributes RecallOnOpen = (FileAttributes)0x0004_0000;

    /// <summary>FILE_ATTRIBUTE_RECALL_ON_DATA_ACCESS.</summary>
    public const FileAttributes RecallOnDataAccess = (FileAttributes)0x0040_0000;

    /// <summary>FILE_ATTRIBUTE_PINNED ("Always keep on this device").</summary>
    public const FileAttributes Pinned = (FileAttributes)0x0008_0000;

    /// <summary>True when reading the file would download it from the cloud.</summary>
    public static bool IsPlaceholder(FileAttributes attributes) =>
        (attributes & (FileAttributes.Offline | RecallOnOpen | RecallOnDataAccess)) != 0;
}
