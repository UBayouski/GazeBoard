using System;
using System.Collections.Generic;
using UnityEngine;

namespace GazeBoard.Board
{
    /// <summary>
    /// Offline word prediction for spelling. Suggests completions for the word being typed from
    /// a frequency-ordered English list (Resources/GazeBoard/words.txt) merged with the words
    /// the user has spoken before, which rank first. Learned words persist in PlayerPrefs.
    /// </summary>
    public class WordPredictor
    {
        private const string ResourcePath = "GazeBoard/words";
        private const string LearnedKey = "gazeboard.learnedWords";
        private const int MaxLearned = 2000;

        private readonly List<string> _words = new List<string>();
        private readonly Dictionary<char, List<string>> _byFirstLetter = new Dictionary<char, List<string>>();
        private readonly Dictionary<string, int> _learned = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        public int DictionarySize => _words.Count;

        public WordPredictor()
        {
            var asset = Resources.Load<TextAsset>(ResourcePath);
            if (asset == null)
            {
                Debug.LogWarning("[GazeBoard] No word list at Resources/" + ResourcePath + "; prediction uses learned words only.");
            }
            else
            {
                foreach (string raw in asset.text.Split('\n'))
                {
                    string word = raw.Trim();
                    if (word.Length == 0) continue;
                    if (word == "i") word = "I";
                    _words.Add(word);
                    char first = char.ToLowerInvariant(word[0]);
                    if (!_byFirstLetter.TryGetValue(first, out List<string> bucket))
                    {
                        bucket = new List<string>();
                        _byFirstLetter[first] = bucket;
                    }
                    bucket.Add(word);
                }
            }
            LoadLearned();
        }

        /// <summary>Up to max completions for the partial word; the user's own words first.</summary>
        public List<string> Suggest(string prefix, int max)
        {
            var result = new List<string>(max);
            prefix = (prefix ?? "").Trim();

            // Learned words first, most used first.
            var learned = new List<KeyValuePair<string, int>>();
            foreach (KeyValuePair<string, int> pair in _learned)
            {
                if (Matches(pair.Key, prefix)) learned.Add(pair);
            }
            learned.Sort((a, b) => b.Value.CompareTo(a.Value));
            foreach (KeyValuePair<string, int> pair in learned)
            {
                if (result.Count >= max) return result;
                AddUnique(result, Cased(pair.Key, prefix));
            }

            if (prefix.Length == 0) return result; // nothing typed yet: only the user's own words

            if (_byFirstLetter.TryGetValue(char.ToLowerInvariant(prefix[0]), out List<string> bucket))
            {
                foreach (string word in bucket)
                {
                    if (result.Count >= max) break;
                    if (Matches(word, prefix)) AddUnique(result, Cased(word, prefix));
                }
            }
            return result;
        }

        /// <summary>Remembers every word of a spoken sentence so it ranks first next time.</summary>
        public void Learn(string sentence)
        {
            if (string.IsNullOrWhiteSpace(sentence)) return;
            bool changed = false;
            foreach (string token in sentence.Split(' '))
            {
                string word = token.Trim().TrimEnd('.', '?', '!', ',');
                if (word.Length < 2 || !IsWordLike(word)) continue;
                _learned.TryGetValue(word, out int count);
                _learned[word] = count + 1;
                changed = true;
            }
            if (changed) SaveLearned();
        }

        private static bool Matches(string word, string prefix)
        {
            if (word.Length <= prefix.Length) return false; // the prefix itself is not a completion
            return word.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
        }

        private static string Cased(string word, string prefix)
        {
            // Keep capitalisation the user started with ("I n" -> "I need").
            if (prefix.Length > 0 && char.IsUpper(prefix[0]) && word.Length > 0 && char.IsLower(word[0]))
            {
                return char.ToUpperInvariant(word[0]) + word.Substring(1);
            }
            return word;
        }

        private static void AddUnique(List<string> list, string word)
        {
            foreach (string w in list)
            {
                if (string.Equals(w, word, StringComparison.OrdinalIgnoreCase)) return;
            }
            list.Add(word);
        }

        private static bool IsWordLike(string s)
        {
            foreach (char c in s)
            {
                if (!char.IsLetter(c) && c != '\'') return false;
            }
            return true;
        }

        // ---- persistence ----

        [Serializable]
        private class LearnedData
        {
            public List<string> words = new List<string>();
            public List<int> counts = new List<int>();
        }

        private void LoadLearned()
        {
            string json = PlayerPrefs.GetString(LearnedKey, "");
            if (string.IsNullOrEmpty(json)) return;
            try
            {
                var data = JsonUtility.FromJson<LearnedData>(json);
                for (int i = 0; i < data.words.Count && i < data.counts.Count; i++)
                {
                    _learned[data.words[i]] = data.counts[i];
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[GazeBoard] Could not read learned words: " + e.Message);
            }
        }

        private void SaveLearned()
        {
            var data = new LearnedData();
            var pairs = new List<KeyValuePair<string, int>>(_learned);
            pairs.Sort((a, b) => b.Value.CompareTo(a.Value));
            for (int i = 0; i < pairs.Count && i < MaxLearned; i++)
            {
                data.words.Add(pairs[i].Key);
                data.counts.Add(pairs[i].Value);
            }
            PlayerPrefs.SetString(LearnedKey, JsonUtility.ToJson(data));
            PlayerPrefs.Save();
        }
    }
}
