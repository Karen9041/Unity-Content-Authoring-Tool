using System.Collections.Generic;

public class FakeContentSource : IContentSource
{
    public ContentLoadResult Load()
    {
        var result = new ContentLoadResult();

        result.Records.Add(new ContentRecord
            {
                Definition = new ContentDefinition
                {
                    Id = "pet_001",
                    Name = "TestCat",
                    EvolutionFromId = null,
                    EvolutionToId = "pet_002",
                    PrefabKey = "Pet_Cat",
                    IconKey = "Icon_Cat"
                },

                Source = new ContentSourceHandle
                {
                    SourceType = "Fake",
                    SourceId = "Fake:pet_001"
                }
            });
        
        result.Records.Add(new ContentRecord
            {
                 Definition = new ContentDefinition
                {
                    Id = "pet_002",
                    Name = "Super Cat",
                    EvolutionFromId = "pet_001",
                    EvolutionToId = "pet_002",
                    PrefabKey = "Pet_SuperCat",
                    IconKey = "Icon_SuperCat"
                },

                Source = new ContentSourceHandle
                {
                    SourceType = "Fake",
                    SourceId = "Fake:pet_002"
                }
            });

        result.Records.Add(new ContentRecord
            {
                Definition = new ContentDefinition
                {
                    Id = "pet_003",
                    Name = "Dog",
                    EvolutionFromId = null,
                    EvolutionToId = "pet_9999",
                    PrefabKey = "Pet_Dog",
                    IconKey = "Icon_Dog"
                },

                Source = new ContentSourceHandle
                {
                    SourceType = "Fake",
                    SourceId = "Fake:pet_003"
                }
            });
        
        result.Records.Add(new ContentRecord
            {
                Definition = new ContentDefinition
                {
                    Id = "pet_003",
                    Name = "DogCopy",
                    EvolutionFromId = null,
                    EvolutionToId = "pet_9999",
                    PrefabKey = "Pet_Dog",
                    IconKey = "Icon_Dog"
                },

                Source = new ContentSourceHandle
                {
                    SourceType = "Fake",
                    SourceId = "Fake:pet_003"
                }
            });

        result.Records.Add(new ContentRecord
            {
                Definition = new ContentDefinition
                {
                    Id = "",
                    Name = "NoId",
                    EvolutionFromId = null,
                    EvolutionToId = null,
                    PrefabKey = "Pet_Dog",
                    IconKey = "Icon_Dog"
                },

                Source = new ContentSourceHandle
                {
                    SourceType = "Fake",
                    SourceId = "Fake:"
                }
            });

        result.Records.Add(new ContentRecord
            {
                Definition = new ContentDefinition
                {
                    Id = "pet_004",
                    Name = "rabbit_A",
                    EvolutionFromId = null,
                    EvolutionToId = "pet_005",
                    PrefabKey = "Pet_Rabbit",
                    IconKey = "Icon_Rabbit"
                },

                Source = new ContentSourceHandle
                {
                    SourceType = "Fake",
                    SourceId = "Fake:pet_004"
                }
            });

        result.Records.Add(new ContentRecord
            {
                Definition = new ContentDefinition
                {
                    Id = "pet_005",
                    Name = "rabbit_B",
                    EvolutionFromId = null,
                    EvolutionToId = "pet_006",
                    PrefabKey = "Pet_Rabbit",
                    IconKey = "Icon_Rabbit"
                },

                Source = new ContentSourceHandle
                {
                    SourceType = "Fake",
                    SourceId = "Fake:pet_005"
                }
            });

        result.Records.Add(new ContentRecord
            {
                Definition = new ContentDefinition
                {
                    Id = "pet_006",
                    Name = "rabbit_C",
                    EvolutionFromId = null,
                    EvolutionToId = "pet_004",
                    PrefabKey = "Pet_Rabbit",
                    IconKey = "Icon_Rabbit"
                },

                Source = new ContentSourceHandle
                {
                    SourceType = "Fake",
                    SourceId = "Fake:pet_006"
                }
            });

        return result;
    }
}