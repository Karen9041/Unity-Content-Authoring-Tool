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
                    EvolutionToId = null,
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
                    EvolutionToId = null,
                    PrefabKey = "Pet_Dog",
                    IconKey = "Icon_Dog"
                },

                Source = new ContentSourceHandle
                {
                    SourceType = "Fake",
                    SourceId = "Fake:pet_003"
                }
            });

        return result;
    }
}