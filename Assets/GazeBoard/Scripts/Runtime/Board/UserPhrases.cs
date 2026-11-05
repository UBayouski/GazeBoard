using System;
using System.Collections.Generic;
using UnityEngine;

namespace GazeBoard.Board
{
    /// <summary>
    /// The user's own content: phrases saved from the sentence bar and the sentences spoken
    /// most recently. Both persist in PlayerPrefs and fill the "Mine" category.
    /// </summary>
    public class UserPhrases
    {
        private const string Key = "gazeboard.userPhrases";
        private const int MaxRecent = 8;
        private const int MaxSaved = 60;

        [Serializable]
        private class Data
        {
            public List<string> saved = new List<string>();
            public List<string> recent = new List<string>();
        }

        private readonly Data _data = new Data();

        public event Action WhenChanged;

        public IReadOnlyList<string> Saved => _data.saved;
        public IReadOnlyList<string> Recent => _data.recent;
        public bool IsEmpty => _data.saved.Count == 0 && _data.recent.Count == 0;

        public UserPhrases()
        {
            string json = PlayerPrefs.GetString(Key, "");
            if (string.IsNullOrEmpty(json)) return;
            try
            {
                Data loaded = JsonUtility.FromJson<Data>(json);
                if (loaded != null)
                {
                    _data.saved = loaded.saved ?? new List<string>();
                    _data.recent = loaded.recent ?? new List<string>();
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[GazeBoard] Could not read saved phrases: " + e.Message);
            }
        }

        /// <summary>Saves a phrase as a tile. Returns false if it was empty or already saved.</summary>
        public bool Save(string phrase)
        {
            phrase = Normalize(phrase);
            if (phrase.Length == 0) return false;
            if (_data.saved.Exists(s => string.Equals(s, phrase, StringComparison.OrdinalIgnoreCase))) return false;

            _data.saved.Insert(0, phrase);
            if (_data.saved.Count > MaxSaved) _data.saved.RemoveAt(_data.saved.Count - 1);
            _data.recent.RemoveAll(s => string.Equals(s, phrase, StringComparison.OrdinalIgnoreCase));
            Persist();
            return true;
        }

        public bool Forget(string phrase)
        {
            int removed = _data.saved.RemoveAll(s => string.Equals(s, phrase, StringComparison.OrdinalIgnoreCase));
            if (removed > 0) Persist();
            return removed > 0;
        }

        /// <summary>Remembers a spoken sentence; saved phrases and board phrases are not repeated here.</summary>
        public void AddRecent(string sentence)
        {
            sentence = Normalize(sentence);
            if (sentence.Length == 0) return;
            if (_data.saved.Exists(s => string.Equals(s, sentence, StringComparison.OrdinalIgnoreCase))) return;

            _data.recent.RemoveAll(s => string.Equals(s, sentence, StringComparison.OrdinalIgnoreCase));
            _data.recent.Insert(0, sentence);
            if (_data.recent.Count > MaxRecent) _data.recent.RemoveAt(_data.recent.Count - 1);
            Persist();
        }

        private static string Normalize(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return "";
            return string.Join(" ", s.Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries));
        }

        private void Persist()
        {
            PlayerPrefs.SetString(Key, JsonUtility.ToJson(_data));
            PlayerPrefs.Save();
            WhenChanged?.Invoke();
        }
    }
}
