namespace Nethermind.Trie.Pruning
{
    public class NullTreeCommitter : ITreeCommitter
    {
        public void Commit(long blockNumber, TrieNode trieNode)
        {
        }

        public void Uncommit()
        {
        }

        public void Flush()
        {
        }

        public byte[] this[byte[] key] => null;
    }
}