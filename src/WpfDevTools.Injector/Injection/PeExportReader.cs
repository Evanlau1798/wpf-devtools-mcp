using System.Text;

namespace WpfDevTools.Injector.Injection;

/// <summary>
/// Reads the PE export table of a native DLL to find the RVA of a named export.
/// Used to compute remote function addresses: remoteBase + RVA.
/// </summary>
public static class PeExportReader
{
    /// <summary>
    /// Get the RVA (Relative Virtual Address) of a named export from a PE file.
    /// </summary>
    public static uint? GetExportRva(string dllPath, string exportName)
    {
        if (string.IsNullOrEmpty(dllPath) || string.IsNullOrEmpty(exportName))
            return null;

        if (!File.Exists(dllPath))
            return null;

        try
        {
            using var stream = File.OpenRead(dllPath);
            using var reader = new BinaryReader(stream);

            // DOS header: MZ signature
            if (reader.ReadUInt16() != 0x5A4D) return null;
            stream.Seek(0x3C, SeekOrigin.Begin);
            var peOffset = reader.ReadUInt32();

            // PE signature
            stream.Seek(peOffset, SeekOrigin.Begin);
            if (reader.ReadUInt32() != 0x00004550) return null;

            // COFF header
            reader.ReadUInt16(); // machine
            var numberOfSections = reader.ReadUInt16();
            stream.Seek(12, SeekOrigin.Current); // TimeDateStamp, PointerToSymbolTable, NumberOfSymbols
            var sizeOfOptionalHeader = reader.ReadUInt16();
            reader.ReadUInt16(); // characteristics

            // Optional header
            var optionalHeaderStart = stream.Position;
            var magic = reader.ReadUInt16();
            bool isPe32Plus = magic == 0x20b;

            // Export directory RVA offset from optional header start
            long exportDirOffset = optionalHeaderStart + (isPe32Plus ? 112 : 96);
            stream.Seek(exportDirOffset, SeekOrigin.Begin);
            var exportDirRva = reader.ReadUInt32();
            reader.ReadUInt32(); // exportDirSize

            if (exportDirRva == 0) return null;

            // Section headers for RVA -> file offset conversion
            stream.Seek(optionalHeaderStart + sizeOfOptionalHeader, SeekOrigin.Begin);
            var sections = new (uint VirtualAddress, uint VirtualSize, uint RawDataOffset)[numberOfSections];
            for (int i = 0; i < numberOfSections; i++)
            {
                stream.Seek(8, SeekOrigin.Current); // Name
                var virtualSize = reader.ReadUInt32();
                var virtualAddress = reader.ReadUInt32();
                reader.ReadUInt32(); // sizeOfRawData
                var pointerToRawData = reader.ReadUInt32();
                stream.Seek(16, SeekOrigin.Current); // remaining fields
                sections[i] = (virtualAddress, virtualSize, pointerToRawData);
            }

            uint RvaToFileOffset(uint rva)
            {
                foreach (var (va, vs, rawOffset) in sections)
                {
                    if (rva >= va && rva < va + vs)
                        return rawOffset + (rva - va);
                }
                return 0;
            }

            // Export directory
            var exportDirFileOffset = RvaToFileOffset(exportDirRva);
            if (exportDirFileOffset == 0) return null;

            stream.Seek(exportDirFileOffset, SeekOrigin.Begin);
            stream.Seek(12, SeekOrigin.Current); // Characteristics, TimeDateStamp, Version
            reader.ReadUInt32(); // nameRva
            reader.ReadUInt32(); // ordinalBase
            reader.ReadUInt32(); // numberOfFunctions
            var numberOfNames = reader.ReadUInt32();
            var addressOfFunctionsRva = reader.ReadUInt32();
            var addressOfNamesRva = reader.ReadUInt32();
            var addressOfNameOrdinalsRva = reader.ReadUInt32();

            var namesFileOffset = RvaToFileOffset(addressOfNamesRva);
            var ordinalsFileOffset = RvaToFileOffset(addressOfNameOrdinalsRva);
            var functionsFileOffset = RvaToFileOffset(addressOfFunctionsRva);

            if (namesFileOffset == 0 || ordinalsFileOffset == 0 || functionsFileOffset == 0)
                return null;

            for (uint i = 0; i < numberOfNames; i++)
            {
                // Read name pointer RVA
                stream.Seek(namesFileOffset + i * 4, SeekOrigin.Begin);
                var namePointerRva = reader.ReadUInt32();
                var nameFileOffset = RvaToFileOffset(namePointerRva);
                if (nameFileOffset == 0) continue;

                // Read null-terminated ASCII name
                stream.Seek(nameFileOffset, SeekOrigin.Begin);
                var nameBytes = new List<byte>();
                byte b;
                while ((b = reader.ReadByte()) != 0)
                    nameBytes.Add(b);
                var name = Encoding.ASCII.GetString(nameBytes.ToArray());

                if (name == exportName)
                {
                    // Read ordinal index
                    stream.Seek(ordinalsFileOffset + i * 2, SeekOrigin.Begin);
                    var ordinal = reader.ReadUInt16();

                    // Read function RVA
                    stream.Seek(functionsFileOffset + ordinal * 4, SeekOrigin.Begin);
                    var functionRva = reader.ReadUInt32();

                    return functionRva;
                }
            }

            return null;
        }
        catch
        {
            return null;
        }
    }
}
