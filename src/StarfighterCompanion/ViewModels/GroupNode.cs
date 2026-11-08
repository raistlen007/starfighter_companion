using System.Collections.ObjectModel;
using StarfighterCompanion.Core.Model;

namespace StarfighterCompanion.ViewModels;

/// <summary>A node of the navigation tree; holds the fields whose group path ends here.</summary>
public sealed class GroupNode
{
    public GroupNode(string name, string path)
    {
        Name = name;
        Path = path;
    }

    public string Name { get; }

    public string Path { get; }

    public ObservableCollection<GroupNode> Children { get; } = new();

    public List<EditableField> Fields { get; } = new();

    public string Title => Fields.Count > 0 ? $"{Name}  ({Fields.Count})" : Name;

    public static List<GroupNode> Build(IEnumerable<EditableField> fields)
    {
        var roots = new List<GroupNode>();
        var byPath = new Dictionary<string, GroupNode>(StringComparer.Ordinal);

        foreach (var field in fields)
        {
            var path = string.IsNullOrEmpty(field.Group) ? "General" : field.Group;
            Node(path).Fields.Add(field);
        }

        return roots;

        GroupNode Node(string path)
        {
            if (byPath.TryGetValue(path, out var existing))
            {
                return existing;
            }

            var cut = path.LastIndexOf('/');
            var node = new GroupNode(cut < 0 ? path : path[(cut + 1)..], path);
            byPath[path] = node;
            if (cut < 0)
            {
                roots.Add(node);
            }
            else
            {
                Node(path[..cut]).Children.Add(node);
            }

            return node;
        }
    }

    public IEnumerable<GroupNode> SelfAndDescendants()
    {
        yield return this;
        foreach (var child in Children)
        {
            foreach (var node in child.SelfAndDescendants())
            {
                yield return node;
            }
        }
    }
}

public sealed class IssueViewModel
{
    public IssueViewModel(ValidationIssue issue) => Issue = issue;

    public ValidationIssue Issue { get; }

    public bool IsError => Issue.Severity == IssueSeverity.Error;

    public string Icon => IsError ? "⛔" : "⚠";

    public string Message => Issue.Message;

    public string Location => Issue.Field is null ? string.Empty : $"{Issue.Field.Group.Replace("/", " › ")} › {Issue.Field.Label}";
}
