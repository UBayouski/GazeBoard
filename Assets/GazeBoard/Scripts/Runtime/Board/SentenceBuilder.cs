using System;
using System.Text;

namespace GazeBoard.Board
{
    /// <summary>
    /// The text being composed in the sentence bar. Phrases are appended as whole words with
    /// spacing handled automatically; letters are appended raw so spelling works.
    /// </summary>
    public class SentenceBuilder
    {
        private readonly StringBuilder _text = new StringBuilder();

        public event Action<string> WhenChanged;

        public string Text => _text.ToString();
        public bool IsEmpty => _text.Length == 0;

        public void AppendPhrase(string phrase)
        {
            if (string.IsNullOrWhiteSpace(phrase)) return;
            if (_text.Length > 0 && _text[_text.Length - 1] != ' ') _text.Append(' ');
            _text.Append(phrase.Trim());
            Notify();
        }

        public void AppendRaw(string s)
        {
            if (string.IsNullOrEmpty(s)) return;
            // Collapse double spaces so Space tiles are forgiving.
            if (s == " " && (_text.Length == 0 || _text[_text.Length - 1] == ' ')) return;
            _text.Append(s);
            Notify();
        }

        /// <summary>Removes the last word, or a trailing letter if the text ends mid-word.</summary>
        public void Backspace()
        {
            if (_text.Length == 0) return;

            // Trim trailing spaces first.
            while (_text.Length > 0 && _text[_text.Length - 1] == ' ') _text.Length--;
            if (_text.Length == 0) { Notify(); return; }

            // If the last character came from spelling (single char token), remove just one char.
            // Otherwise remove the whole last word.
            int lastSpace = Text.LastIndexOf(' ');
            int wordStart = lastSpace + 1;
            int wordLength = _text.Length - wordStart;
            _text.Length = wordLength <= 1 ? _text.Length - 1 : wordStart;

            while (_text.Length > 0 && _text[_text.Length - 1] == ' ') _text.Length--;
            Notify();
        }

        public void Clear()
        {
            if (_text.Length == 0) return;
            _text.Clear();
            Notify();
        }

        private void Notify() => WhenChanged?.Invoke(Text);
    }
}
