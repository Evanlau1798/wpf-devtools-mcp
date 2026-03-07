using WpfDevTools.Shared.Enums;

namespace WpfDevTools.Injector.Discovery;

/// <summary>
/// Reads PE headers to determine DLL architecture, with managed AnyCPU detection.
/// Returns ProcessArchitecture.Unknown for AnyCPU assemblies (compatible with any arch).
/// </summary>
public static class PeArchitectureReader
{
    private const ushort MzMagic = 0x5A4D;
    private const uint PeSignature = 0x00004550;
    private const ushort MachineI386 = 0x014C;
    private const ushort MachineAmd64 = 0x8664;
    private const ushort MachineArm64 = 0xAA64;
    private const ushort Pe32Magic = 0x010B;
    private const ushort Pe32PlusMagic = 0x020B;
    private const int CoffHeaderSize = 20;
    private const int ClrDataDirectoryIndex = 14;
    private const uint CorFlagsIlOnly = 0x00000001;
    private const uint CorFlags32BitRequired = 0x00000002;

    /// <summary>
    /// Detect DLL architecture from PE headers.
    /// For managed AnyCPU assemblies (ILONLY without 32BITREQUIRED), returns Unknown
    /// to indicate compatibility with any architecture.
    /// </summary>
    public static ProcessArchitecture Detect(string dllPath)
    {
        try
        {
            if (string.IsNullOrEmpty(dllPath) || !File.Exists(dllPath))
                return ProcessArchitecture.Unknown;

            using var stream = new FileStream(dllPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var reader = new BinaryReader(stream);

            // DOS header: magic number "MZ"
            if (stream.Length < 64 || reader.ReadUInt16() != MzMagic)
                return ProcessArchitecture.Unknown;

            // PE header offset at 0x3C
            stream.Seek(0x3C, SeekOrigin.Begin);
            var peOffset = reader.ReadInt32();

            if (peOffset < 0 || peOffset + 4 > stream.Length)
                return ProcessArchitecture.Unknown;

            // PE signature: "PE\0\0"
            stream.Seek(peOffset, SeekOrigin.Begin);
            if (reader.ReadUInt32() != PeSignature)
                return ProcessArchitecture.Unknown;

            // COFF header: Machine type (2 bytes)
            var machine = reader.ReadUInt16();

            var architecture = machine switch
            {
                MachineI386 => ProcessArchitecture.X86,
                MachineAmd64 => ProcessArchitecture.X64,
                MachineArm64 => ProcessArchitecture.ARM64,
                _ => ProcessArchitecture.Unknown
            };

            // For I386, check if it's actually a managed AnyCPU assembly
            if (machine == MachineI386)
            {
                if (IsManagedAnyCpu(reader, stream, peOffset))
                {
                    return ProcessArchitecture.Unknown;
                }
            }

            return architecture;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"PeArchitectureReader: Failed to read PE header for '{dllPath}': {ex.Message}");
            return ProcessArchitecture.Unknown;
        }
    }

    /// <summary>
    /// Check if PE file is a managed AnyCPU assembly by examining CLR metadata.
    /// Assumes reader is positioned after machine type in COFF header.
    /// </summary>
    private static bool IsManagedAnyCpu(BinaryReader reader, FileStream stream, int peOffset)
    {
        try
        {
            // COFF header layout (after PE signature + machine type):
            //   NumberOfSections: 2 bytes (at peOffset+6)
            //   TimeDateStamp: 4 bytes
            //   PointerToSymbolTable: 4 bytes
            //   NumberOfSymbols: 4 bytes
            //   SizeOfOptionalHeader: 2 bytes (at peOffset+20)
            //   Characteristics: 2 bytes

            stream.Seek(peOffset + 6, SeekOrigin.Begin);
            var numberOfSections = reader.ReadUInt16();

            stream.Seek(peOffset + 20, SeekOrigin.Begin);
            var sizeOfOptionalHeader = reader.ReadUInt16();

            if (sizeOfOptionalHeader == 0)
                return false;

            // Optional header starts at peOffset + 24
            var optionalHeaderOffset = peOffset + 24;
            stream.Seek(optionalHeaderOffset, SeekOrigin.Begin);
            var optionalMagic = reader.ReadUInt16();

            // Calculate CLR data directory offset based on PE32 vs PE32+
            int dataDirectoriesOffset;
            if (optionalMagic == Pe32Magic)
            {
                // PE32: data directories start at optional header + 96
                dataDirectoriesOffset = optionalHeaderOffset + 96;
            }
            else if (optionalMagic == Pe32PlusMagic)
            {
                // PE32+: data directories start at optional header + 112
                dataDirectoriesOffset = optionalHeaderOffset + 112;
            }
            else
            {
                return false;
            }

            // CLR Runtime Header is data directory index 14
            // Each data directory entry is 8 bytes (4 RVA + 4 Size)
            var clrDirectoryOffset = dataDirectoriesOffset + (ClrDataDirectoryIndex * 8);

            if (clrDirectoryOffset + 8 > stream.Length)
                return false;

            stream.Seek(clrDirectoryOffset, SeekOrigin.Begin);
            var clrRva = reader.ReadUInt32();
            var clrSize = reader.ReadUInt32();

            if (clrRva == 0 || clrSize == 0)
                return false; // Not a managed assembly

            // Resolve CLR RVA to file offset using section table
            var sectionTableOffset = optionalHeaderOffset + sizeOfOptionalHeader;
            var clrFileOffset = RvaToFileOffset(reader, stream, clrRva, sectionTableOffset, numberOfSections);

            if (clrFileOffset < 0 || clrFileOffset + 16 > stream.Length)
                return false;

            // Read CLR header (IMAGE_COR20_HEADER)
            // Layout: cb (4), MajorRuntimeVersion (2), MinorRuntimeVersion (2),
            //         MetaData RVA (4), MetaData Size (4), Flags (4)
            stream.Seek(clrFileOffset + 16, SeekOrigin.Begin);
            var corFlags = reader.ReadUInt32();

            // AnyCPU: ILONLY is set, 32BITREQUIRED is NOT set
            var isIlOnly = (corFlags & CorFlagsIlOnly) != 0;
            var is32BitRequired = (corFlags & CorFlags32BitRequired) != 0;

            return isIlOnly && !is32BitRequired;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Convert RVA (Relative Virtual Address) to file offset using section table
    /// </summary>
    private static long RvaToFileOffset(
        BinaryReader reader, FileStream stream,
        uint rva, int sectionTableOffset, ushort numberOfSections)
    {
        // Each section header is 40 bytes
        // Layout: Name (8), VirtualSize (4), VirtualAddress (4),
        //         SizeOfRawData (4), PointerToRawData (4), ...
        for (int i = 0; i < numberOfSections; i++)
        {
            var sectionOffset = sectionTableOffset + (i * 40);
            if (sectionOffset + 40 > stream.Length)
                return -1;

            stream.Seek(sectionOffset + 12, SeekOrigin.Begin); // VirtualAddress
            var virtualAddress = reader.ReadUInt32();
            var sizeOfRawData = reader.ReadUInt32();
            var pointerToRawData = reader.ReadUInt32();

            // Read VirtualSize for bounds checking
            stream.Seek(sectionOffset + 8, SeekOrigin.Begin);
            var virtualSize = reader.ReadUInt32();

            if (rva >= virtualAddress && rva < virtualAddress + Math.Max(virtualSize, sizeOfRawData))
            {
                return pointerToRawData + (rva - virtualAddress);
            }
        }

        return -1;
    }
}
