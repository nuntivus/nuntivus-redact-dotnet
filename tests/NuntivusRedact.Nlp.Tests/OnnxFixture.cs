using System.Security.Cryptography;
using System.Text;
using NuntivusRedact.Nlp.Onnx;

namespace NuntivusRedact.Nlp.Tests;

// A tiny Gather graph exercises real native inference, not trained-model accuracy.
internal sealed class OnnxFixture : IDisposable
{
    internal static readonly string[] Labels = ["O", "B-PER", "I-PER", "B-ORG", "I-ORG", "B-LOC", "I-LOC"];
    internal static readonly string[] Vocabulary = ["[PAD]", "[UNK]", "[CLS]", "[SEP]", "[MASK]", "Alice", "Smith",
        "works", "at", "Contoso", ".", "東", "京", "Mario", "Rossi", "José", "Ирина", "joins", "##son", "John", "张", "伟", "Annie"];
    private readonly string directory = Path.Combine(Path.GetTempPath(), "nuntivus-nlp-" + Guid.NewGuid().ToString("N"));

    internal OnnxFixture(bool nonfinite = false, string outputName = "logits", bool tokenTypes = false)
    {
        Directory.CreateDirectory(directory);
        ModelPath = Path.Combine(directory, "model.onnx");
        VocabularyPath = Path.Combine(directory, "vocab.txt");
        File.WriteAllText(VocabularyPath, string.Join('\n', Vocabulary) + "\n", new UTF8Encoding(false));
        File.WriteAllBytes(ModelPath, BuildModel(nonfinite, outputName, tokenTypes));
        ModelHash = Hash(ModelPath);
        VocabularyHash = Hash(VocabularyPath);
    }

    internal string ModelPath { get; }
    internal string VocabularyPath { get; }
    internal string ModelHash { get; }
    internal string VocabularyHash { get; }

    internal OnnxNerOptions Options(int sequence = 8, int overlap = 2, int characters = 100_000, int tokens = 20_000,
        int windows = 256, int entities = 1_000, int requests = 16, string? modelHash = null, int modelBytes = 1_000_000_000) =>
        new(ModelPath, VocabularyPath, modelHash ?? ModelHash, VocabularyHash, Labels,
            sequence, overlap, characters, tokens, windows, entities, requests, modelBytes);

    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    private static byte[] BuildModel(bool nonfinite, string outputName, bool tokenTypes)
    {
        int[] predicted = [0, 0, 0, 0, 0, 1, 2, 0, 0, 3, 0, 5, 6, 1, 2, 1, 1, 0, 2, 1, 1, 2, 1];
        var floats = new float[Vocabulary.Length * Labels.Length];
        for (int i = 0; i < Vocabulary.Length; i++) floats[i * Labels.Length + predicted[i]] = nonfinite ? float.NaN : 12;
        byte[] raw = new byte[floats.Length * sizeof(float)];
        Buffer.BlockCopy(floats, 0, raw, 0, raw.Length);
        var initializer = Message(Varint(1, (ulong)Vocabulary.Length), Varint(1, (ulong)Labels.Length),
            Varint(2, 1), Text(8, "weights"), Bytes(9, raw));
        var node = Message(Text(1, "weights"), Text(1, "input_ids"), Text(2, outputName), Text(4, "Gather"));
        var graph = Message(Bytes(1, node), Text(2, "synthetic_ner"), Bytes(5, initializer),
            Bytes(11, ValueInfo("input_ids", 7, false)), Bytes(11, ValueInfo("attention_mask", 7, false)),
            tokenTypes ? Bytes(11, ValueInfo("token_type_ids", 7, false)) : [],
            Bytes(12, ValueInfo(outputName, 1, true)));
        return Message(Varint(1, 8), Bytes(7, graph), Bytes(8, Message(Varint(2, 13))));
    }

    private static byte[] ValueInfo(string name, ulong type, bool output)
    {
        var shape = Message(Bytes(1, Varint(1, 1)), Bytes(1, Text(2, "sequence")),
            output ? Bytes(1, Varint(1, (ulong)Labels.Length)) : []);
        return Message(Text(1, name), Bytes(2, Bytes(1, Message(Varint(1, type), Bytes(2, shape)))));
    }

    private static byte[] Text(int field, string value) => Bytes(field, Encoding.UTF8.GetBytes(value));
    private static byte[] Bytes(int field, byte[] value) => Message(Unsigned((ulong)(field << 3 | 2)), Unsigned((ulong)value.Length), value);
    private static byte[] Varint(int field, ulong value) => Message(Unsigned((ulong)(field << 3)), Unsigned(value));
    private static byte[] Message(params byte[][] pieces) => pieces.SelectMany(piece => piece).ToArray();
    private static byte[] Unsigned(ulong value)
    {
        var bytes = new List<byte>();
        while (value >= 128) { bytes.Add((byte)(value | 128)); value >>= 7; }
        bytes.Add((byte)value);
        return bytes.ToArray();
    }

    public void Dispose() => Directory.Delete(directory, true);
}
