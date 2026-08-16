using Lumina.Excel;

namespace CelinesChat.Services;

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
