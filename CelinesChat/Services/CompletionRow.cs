using Lumina.Excel;

namespace CelinesChat.Services;

/// <summary>
/// A row from the game's "Completion" sheet - the same data the native chat box's Tab
/// auto-translate popup is built from (dungeon names, greetings, actions, and so on). There's no
/// pre-generated Lumina row type for this sheet bundled with Dalamud, so this reads columns at
/// fixed byte offsets instead.
///
/// Those offsets are NOT simply page.Sheet.Columns[schemaFieldIndex] - the schema
/// (xivdev/EXDSchema, Completion.yml) lists fields in the logical order Text, GroupTitle,
/// LookupTable, Group, Key, but the raw column definitions Lumina reports at runtime come back
/// in a different order entirely ([0]=UInt16@12, [1]=UInt16@14, [2]=String@8, [3]=String@0,
/// [4]=String@4 - confirmed via a live diagnostic dump). Sorted by byte offset, that's exactly
/// Text@0, GroupTitle@4, LookupTable@8, Group@12, Key@14, matching the schema's field order.
///
/// The "Key" column (offset 14) is intentionally NOT exposed here - AutoTranslatePayload's
/// second argument is each row's own RowId (its intrinsic Excel row number, already available
/// via IExcelRow<T>.RowId below), not this data column. Verified against Chat2's own working
/// auto-translate code (ChatTwo/Ui/Handler/AutoCompleteHandler.cs), which builds its replacement
/// as "&lt;at:{entry.Group},{entry.Row}&gt;" using the row ID, never a separate key field -
/// confusing this "Key" column for that row ID field is what sent the wrong phrase every time.
/// </summary>
[Sheet("Completion")]
internal readonly struct CompletionRow : IExcelRow<CompletionRow>
{
    private readonly ExcelPage page;
    private readonly uint offset;

    private CompletionRow(ExcelPage page, uint offset, uint row)
    {
        this.page = page;
        this.offset = offset;
        RowId = row;
    }

    public uint RowId { get; }

    public ExcelPage ExcelPage => page;

    public uint RowOffset => offset;

    public string Text => page.ReadString(offset + 0u, offset).ExtractText();

    public string GroupTitle => page.ReadString(offset + 4u, offset).ExtractText();

    public uint Group => page.ReadUInt16(offset + 12u);

    static CompletionRow IExcelRow<CompletionRow>.Create(ExcelPage page, uint offset, uint row) => new(page, offset, row);
}
