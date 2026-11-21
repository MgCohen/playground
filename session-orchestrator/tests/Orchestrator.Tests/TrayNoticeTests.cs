using Orchestrator.Core.Contract;
using Orchestrator.Web;
using Orchestrator.Web.Components.Tray;

namespace Orchestrator.Tests;

/// <summary>The rule of the badge notice: one notice for each card that arrives while the tray is closed, and never again.</summary>
public sealed class TrayNoticeTests
{
    static string Session(Rig rig) => rig.Store.Create(new Seed { Title = "Notice", Cwd = "C:/x" }, "start");

    static void Ask(Rig rig, string session, string id) =>
        rig.Store.SaveDecision(new Decision(id, session) { Question = $"Question {id}?", Options = ["A", "B"], Risk = "medium" });

    static IReadOnlyList<CardView> Cards(Rig rig) => new Board(rig.Store, rig.Supervisor).Cards();

    [Test]
    public void The_cards_at_the_start_get_no_notice()
    {
        using var rig = new Rig();
        var s = Session(rig);
        Ask(rig, s, "a1");
        var notices = new TrayNotices();

        Assert.That(notices.Take(Cards(rig), closed: true), Is.Empty);
    }

    [Test]
    public void A_card_that_arrives_while_the_tray_is_closed_gets_one_notice()
    {
        using var rig = new Rig();
        var s = Session(rig);
        var notices = new TrayNotices();
        notices.Take(Cards(rig), closed: true);

        Ask(rig, s, "a1");
        Assert.That(notices.Take(Cards(rig), closed: true).Select(c => c.Title), Is.EqualTo(new[] { "Question a1?" }));
        Assert.That(notices.Take(Cards(rig), closed: true), Is.Empty, "the card is done after its notice");

        Ask(rig, s, "a2");
        Assert.That(notices.Take(Cards(rig), closed: true).Select(c => c.Title), Is.EqualTo(new[] { "Question a2?" }), "only the new card");
    }

    [Test]
    public void A_card_that_arrives_while_the_panel_shows_never_gets_a_notice()
    {
        using var rig = new Rig();
        var s = Session(rig);
        var notices = new TrayNotices();
        notices.Take(Cards(rig), closed: false);

        Ask(rig, s, "a1");
        Assert.That(notices.Take(Cards(rig), closed: false), Is.Empty, "the panel shows the card");
        Assert.That(notices.Take(Cards(rig), closed: true), Is.Empty, "a minimize brings no notice for a card that is already in the app");
    }
}
