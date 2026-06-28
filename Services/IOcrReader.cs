namespace HydrusComicCompanion.Services
{
    public interface IOcrReader
    {
        public OcrData ReadOcrDataForFile(string path);
        public string ReadOcrPlaintextForFile(string path, IEnumerable<string>? textTypesToExclude = null);
    }

    public struct OcrData
    {
        public List<Segment> Segments { get; init; }
        public List<Block> Blocks { get; init; }
    }

    public struct Segment
    {
        public string Uid { get; init; }
        public string Text { get; init; }
        public List<Corner> Corners { get; init; }
    }

    public struct Corner
    {
        public int X { get; init; }
        public int Y { get; init; }
    }

    public struct Block
    {
        public string Uid { get; init; }
        public int Order { get; init; }
        public List<string> SegmentIds { get; init; }
        public string? TextType { get; init; }
    }
}
