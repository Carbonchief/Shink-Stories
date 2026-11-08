namespace Shink.Components.Content;

public sealed record StoreProductCollection(string Slug, string Name, string EnglishName);

public static class StoreProductCollections
{
    public const string Books = "kinderboeke";
    public const string StoryBuddies = "storietjommies";
    public const string Bundles = "pakkette";

    public static IReadOnlyList<StoreProductCollection> All { get; } =
    [
        new(Books, "Kinderboeke", "Children's books"),
        new(StoryBuddies, "StorieTjommies", "Story buddies"),
        new(Bundles, "Pakkette", "Bundles")
    ];

    public static StoreProductCollection? Find(string? slug) => All.FirstOrDefault(collection =>
        string.Equals(collection.Slug, slug?.Trim(), StringComparison.OrdinalIgnoreCase));

    public static StoreProductCollection GetOrDefault(string? slug) => Find(slug) ?? All[1];
}
