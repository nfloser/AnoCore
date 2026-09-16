using AnoCore.Abstractions.Menus;
using AnoCore.Abstractions.Players;
using AnoCore.Abstractions.Voting;
namespace AnoCore.Runtime.Voting;

public static class VoteMenuFactory
{
    public static MenuDefinition Create(VoteSnapshot vote, Func<PlayerId, string, CancellationToken, ValueTask> castVote)
    {
        ArgumentNullException.ThrowIfNull(vote); ArgumentNullException.ThrowIfNull(castVote);
        return new MenuDefinition(new MenuId(vote.Definition.Id.Value), vote.Definition.Title, vote.Definition.Options.Select(option => new MenuOption(option.Id, option.Label, context => castVote(context.PlayerId, option.Id, context.CancellationToken))).ToArray());
    }
}
