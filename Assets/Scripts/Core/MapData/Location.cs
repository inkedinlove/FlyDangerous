﻿#nullable enable
using System.Collections.Generic;
using Misc;

namespace Core.MapData {
    public class Location : IFdEnum {
        private static int _id;

        // Declare locations here and add to the List() function below
        public static readonly Location TerrainV3 = new("Mixed Terrain",
            "A terrain with multiple mixed environments blended together over very large distances with water mechanics", "TerrainWorkspace", true);

        public static readonly Location TestSpaceStation = new("Space Station", "An enormous test space station asset", "SpaceStation", false);

        public static readonly Location ProvingGrounds = new("Proving Grounds",
            "A testing scene used for staging new features and testing flight mechanics", "ProvingGrounds", false);

        public static readonly Location Space = new("Space", "Empty space - literally nothing here", "Space", false);

        public static readonly Location TerrainV1 = new("Mountains (Legacy)",
            "Terrain with peaks no higher than 2km - only here for compatibility with legacy maps", "TerrainV1", true);

        public static readonly Location TerrainV2 = new("Canyons (Legacy)",
            "Terrain with peaks of 8km and deep, straight canyon grooves - only here for compatibility with legacy maps", "TerrainV2", true);


        private Location(string name, string description, string sceneToLoad, bool isTerrain) {
            Id = GenerateId;
            Name = name;
            Description = description;
            SceneToLoad = sceneToLoad;
            IsTerrain = isTerrain;
        }

        private static int GenerateId => _id++;

        public string SceneToLoad { get; }
        public bool IsTerrain { get; }
        public string Description { get; }
        public int Id { get; }
        public string Name { get; }

        public static IEnumerable<Location> List() {
            return new[] { TerrainV3, TestSpaceStation, ProvingGrounds, Space, TerrainV1, TerrainV2 };
        }

        public static Location FromString(string locationString) {
            return FdEnum.FromString(List(), locationString);
        }

        public static Location FromId(int id) {
            return FdEnum.FromId(List(), id);
        }
    }
}