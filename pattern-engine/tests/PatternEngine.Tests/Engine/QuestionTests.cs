using PatternEngine.Features.Cases.Domain.Shared;
using PatternEngine.Features.Eval.Domain;
using PatternEngine.Features.Grading.Domain;
using PatternEngine.Features.Grading.Domain.Shared;
using PatternEngine.Features.Trace.Domain.Shared;

namespace PatternEngine.Tests.Engine;

/// <summary>Questions to the user, with a scripted judge and a simulated user. No model.</summary>
public sealed class QuestionTests
{
    private static readonly Case Sign = new()
    {
        Id = "ask",
        Source = "test",
        Tags = ["ask"],
        Request = "Draw a sign.",
        Hidden = new() { ["board_fill"] = "green" },
        Expect = new Expect { Outcome = Outcomes.Match, Tree = new TreeNode { P = "sign", Params = new() { ["board_fill"] = "green" } }, Asks = ["board_fill"] },
    };

    [Test]
    public async Task A_missing_required_input_is_asked_and_the_answer_is_used()
    {
        var run = await ShapesScript.RunAsync(Sign.Request, ShapesScript.Judge(), user: new SimulatedUser(Sign));
        Assert.That(run.Trace.Questions, Has.Count.EqualTo(1));
        Assert.That(run.Trace.Questions[0].Key, Is.EqualTo("board_fill"));
        Assert.That(run.Trace.Questions[0].Answer, Is.EqualTo("green"));
        Assert.That(run.Trace.Tree!.ToString(), Is.EqualTo("sign(plain){board_fill:green}"));
        var grades = EngineTests.Grade(run, Sign.Expect, Sign.Hidden);
        Assert.That(grades.Verdicts.Where(v => !v.Value.Ok).Select(v => $"{v.Key}: {v.Value.Failures[0]}"), Is.Empty);
    }

    [Test]
    public async Task With_no_user_the_question_stays_open_and_the_outcome_is_ask()
    {
        var run = await ShapesScript.RunAsync(Sign.Request, ShapesScript.Judge());
        Assert.That(run.Trace.Outcome, Is.EqualTo(Outcomes.Ask));
        Assert.That(run.Trace.Questions.Single().Answer, Is.Null);
        Assert.That(run.Stats.Calls, Is.EqualTo(1), "no compose call while a question is open");
    }

    [Test]
    public async Task A_value_in_the_request_is_not_asked()
    {
        var judge = ShapesScript.Judge(call => call.Agent == "decider"
            ? new { status = "solved", pattern = "sign", variant = "plain", @params = new Dictionary<string, string> { ["board_fill"] = "Red" }, reason = "named" }
            : null);
        var run = await ShapesScript.RunAsync("Draw a sign with a red board.", judge);
        Assert.That(run.Trace.Questions, Is.Empty);
        Assert.That(run.Trace.Tree!.Params!["board_fill"], Is.EqualTo("red"));
    }

    [Test]
    public async Task An_unclear_request_escalates_and_the_answer_decides_it()
    {
        var c = new Case
        {
            Id = "unclear",
            Source = "test",
            Tags = ["ask"],
            Request = "Draw a building.",
            Hidden = new() { ["pattern choice"] = "house" },
            Expect = new Expect { Outcome = Outcomes.Match, Tree = new TreeNode { P = "house" }, Asks = ["pattern choice"] },
        };
        var judge = ShapesScript.Judge(call => call.Agent switch
        {
            "decider" when ShapesScript.Request(call.Input) == "Draw a building." => new { status = "unclear", pattern = "house", reason = "house, tower and shed fit" },
            "escalation" => new
            {
                action = "question",
                question = new { about = "pattern choice", key = "building", text = "Which building: a house, a tower or a shed?", options = new[] { "house", "tower", "shed" } },
                reason = "three patterns fit",
            },
            _ => null,
        });
        var run = await ShapesScript.RunAsync(c.Request, judge, user: new SimulatedUser(c));
        Assert.That(run.Trace.Tree!.P, Is.EqualTo("house"));
        Assert.That(run.Trace.Notes, Has.Some.Contains("escalation"));
        var grades = EngineTests.Grade(run, c.Expect, c.Hidden);
        Assert.That(grades.Verdicts.Where(v => !v.Value.Ok).Select(v => $"{v.Key}: {v.Value.Failures[0]}"), Is.Empty);
    }

    [Test]
    public async Task A_request_outside_the_domain_ends_with_a_question()
    {
        var judge = ShapesScript.Judge(call => call.Agent switch
        {
            "decider" => new { status = "outside", pattern = "house", reason = "a poem is not a drawing" },
            "escalation" => new { action = "question", question = new { about = "domain", key = "domain", text = "I can only draw simple pictures. What do you want me to draw?" }, reason = "outside" },
            _ => null,
        });
        var run = await ShapesScript.RunAsync("Write a poem.", judge);
        Assert.That(run.Trace.Outcome, Is.EqualTo(Outcomes.Ask));
        var c = new Case { Id = "o", Source = "t", Tags = [], Request = "Write a poem.", Expect = new Expect { Outcome = Outcomes.Ask, Asks = ["domain"] } };
        var grades = EngineTests.Grade(run, c.Expect);
        Assert.That(grades.Verdicts.Where(v => !v.Value.Ok).Select(v => $"{v.Key}: {v.Value.Failures[0]}"), Is.Empty);
    }

    [Test]
    public void An_extra_question_fails_the_questions_grader()
    {
        var trace = new RunTrace
        {
            Outcome = Outcomes.Match,
            Questions = [new Question { About = QuestionTopics.Input, Key = "door_fill", Text = "Which door color?", Answer = null }],
        };
        var c = new Case { Id = "x", Source = "t", Tags = [], Request = "Draw a house.", Expect = new Expect { Outcome = Outcomes.Match } };
        var v = new QuestionsGrader().Grade(new GradeInput { Case = c, Catalog = ShapesScript.Pack().Catalog, Pack = new Domains.Shapes.ShapesPack(), Trace = trace });
        Assert.That(v.Ok, Is.False);
        Assert.That(v.Failures[0], Does.StartWith("extra question"));
    }

    [Test]
    public void A_needed_question_that_was_not_asked_fails()
    {
        var trace = new RunTrace { Outcome = Outcomes.Match, Tree = new TreeNode { P = "sign", Params = new() { ["board_fill"] = "red" } } };
        var v = new QuestionsGrader().Grade(new GradeInput { Case = Sign, Catalog = ShapesScript.Pack().Catalog, Pack = new Domains.Shapes.ShapesPack(), Trace = trace });
        Assert.That(v.Failures, Has.Some.Contains("did not ask for \"board_fill\""));
    }
}
