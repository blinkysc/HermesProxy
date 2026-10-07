using System;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using HermesProxy.World.Enums;

namespace HermesProxy.World.Server;

/// <summary>
/// Rewrites the account data a 3.3.5a client saved on the server into the format the modern
/// client reads, so keybindings and macros made before the proxy carry over.
/// </summary>
/// <remarks>
/// Two formats changed. Macros: the 3.3.5a header <c>MACRO id "name" icon</c> became
/// <c>VER 3 id-in-hex "name" "icon"</c>. Bindings: the leading <c>BINDINGMODE</c> line is gone.
/// Anything already in the modern format, or not parsable, passes through unchanged.
/// </remarks>
public static class LegacyAccountDataConverter
{
    private const string LegacyMacroPrefix = "MACRO ";
    private const string BindingModePrefix = "BINDINGMODE";

    public static (uint UncompressedSize, byte[] CompressedData) ToModern(uint type, uint uncompressedSize, byte[] compressed)
    {
        if (uncompressedSize == 0 || !HasLegacyFormat(type))
            return (uncompressedSize, compressed);

        string text;
        try
        {
            text = Decompress(compressed);
        }
        catch (InvalidDataException)
        {
            return (uncompressedSize, compressed);
        }

        string? converted = ConvertText(type, text);
        if (converted == null)
            return (uncompressedSize, compressed);

        byte[] raw = Encoding.UTF8.GetBytes(converted);
        return ((uint)raw.Length, Compress(raw));
    }

    private static bool HasLegacyFormat(uint type) => type is >= 2 and <= 5;

    /// <returns>The converted text, or null when there is nothing to convert.</returns>
    public static string? ConvertText(uint type, string text) => (AccountDataType)type switch
    {
        AccountDataType.GlobalMacrosCache or AccountDataType.PerCharacterMacrosCache => ConvertMacros(text),
        AccountDataType.GlobalBindingsCache or AccountDataType.PerCharacterBindingsCache => ConvertBindings(text),
        _ => null,
    };

    private static string? ConvertMacros(string text)
    {
        if (!text.StartsWith(LegacyMacroPrefix, StringComparison.Ordinal))
            return null;

        var result = new StringBuilder(text.Length + 64);
        foreach (string line in SplitLines(text))
        {
            if (line.StartsWith(LegacyMacroPrefix, StringComparison.Ordinal)
                && TryParseLegacyHeader(line, out long id, out string name, out string icon))
                result.Append(CultureInfo.InvariantCulture, $"VER 3 {(ulong)id:X16} \"{name}\" \"{icon}\"\n");
            else
                result.Append(line).Append('\n');
        }
        return result.ToString();
    }

    private static bool TryParseLegacyHeader(string line, out long id, out string name, out string icon)
    {
        id = 0;
        name = icon = string.Empty;

        int idEnd = line.IndexOf(' ', LegacyMacroPrefix.Length);
        if (idEnd < 0 || !long.TryParse(line.AsSpan(LegacyMacroPrefix.Length, idEnd - LegacyMacroPrefix.Length),
                NumberStyles.Integer, CultureInfo.InvariantCulture, out id))
            return false;

        int nameStart = line.IndexOf('"', idEnd);
        int nameEnd = nameStart < 0 ? -1 : line.IndexOf('"', nameStart + 1);
        if (nameEnd < 0)
            return false;

        name = line.Substring(nameStart + 1, nameEnd - nameStart - 1);
        icon = line.Substring(nameEnd + 1).Trim().Trim('"');
        return true;
    }

    private static string? ConvertBindings(string text)
    {
        if (!text.StartsWith(BindingModePrefix, StringComparison.Ordinal))
            return null;

        var result = new StringBuilder(text.Length);
        foreach (string line in SplitLines(text))
        {
            if (!line.StartsWith(BindingModePrefix, StringComparison.Ordinal))
                result.Append(line).Append('\n');
        }
        return result.ToString();
    }

    private static string[] SplitLines(string text) => text.Replace("\r\n", "\n").TrimEnd('\n', '\0').Split('\n');

    private static string Decompress(byte[] compressed)
    {
        using var input = new MemoryStream(compressed);
        using var zlib = new ZLibStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        zlib.CopyTo(output);
        return Encoding.UTF8.GetString(output.GetBuffer(), 0, (int)output.Length).TrimEnd('\0');
    }

    public static byte[] Compress(byte[] raw)
    {
        using var output = new MemoryStream();
        using (var zlib = new ZLibStream(output, CompressionLevel.Optimal))
            zlib.Write(raw);
        return output.ToArray();
    }
}
