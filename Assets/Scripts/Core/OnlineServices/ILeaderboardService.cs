using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Core.Player;

namespace Core.OnlineServices {
    public interface ILeaderboardEntry {
        public int Rank { get; }
        public string Player { get; }
        public int Score { get; }

        public Flag Flag { get; }

        public Task<IOnlineFile> Replay();
    }

    public interface ILeaderboard {
        // TODO: Handle pagination - for now let's just show the top 20 and call it a day (we need lots of entries to properly test)
        public Task<List<ILeaderboardEntry>> GetEntries();
        public Task UploadScore(int score, Flag flag);
    }

    public interface ILeaderboardService {
        public Task<ILeaderboard> FindOrCreateLeaderboard(string id);
    }

    public interface IOnlineFile {
        public string Filename { get; }
        public MemoryStream Data { get; }
    }
}