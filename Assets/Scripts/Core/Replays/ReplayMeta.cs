﻿using Core.MapData;
using Newtonsoft.Json;

namespace Core.Replays {
    public class ReplayMeta {
        [JsonConstructor]
        private ReplayMeta(string version, int keyFrameIntervalTicks, int keyFrameBufferSizeBytes, int inputFrameBufferSizeBytes, string levelHash) {
            Version = version;
            KeyFrameIntervalTicks = keyFrameIntervalTicks;
            KeyFrameBufferSizeBytes = keyFrameBufferSizeBytes;
            InputFrameBufferSizeBytes = inputFrameBufferSizeBytes;
        }

        public string Version { get; }
        public int KeyFrameIntervalTicks { get; }
        public int KeyFrameBufferSizeBytes { get; }
        public int InputFrameBufferSizeBytes { get; }
        public string LevelHash { get; set; }

        public string ToJsonString() {
            return JsonConvert.SerializeObject(this, Formatting.Indented);
        }

        public static ReplayMeta FromJsonString(string json) {
            return JsonConvert.DeserializeObject<ReplayMeta>(json);
        }

        public static ReplayMeta Version100(LevelData levelData) {
            return new ReplayMeta("1.0.0", 25, 86, 39, levelData.LevelHash());
        }
    }
}