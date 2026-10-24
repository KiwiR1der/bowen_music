namespace Bowen.Core.Services.Abstractions;

public interface ISearchHistoryStore
{
    IReadOnlyList<string> Load();
    Task SaveAsync(IReadOnlyList<string> keywords);
}
