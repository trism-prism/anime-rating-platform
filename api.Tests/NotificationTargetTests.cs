using api.Models;
using static api.Tests.ReviewTests;
using static api.Tests.SocialTests;

namespace api.Tests;

// the bell names the review (its title) and what was reported, so a row can say what it is about
public class NotificationTargetTests
{
    [Fact]
    public async Task Rows_carry_the_title_and_content_type()
    {
        var open = Mem();
        await Seed(open);
        var review = await AddRating(open, "a", 2, "text");
        using (var db = open())
        {
            var r = new Reply { RatingId = review, UserId = "b", Body = "hi" };
            var rep = new Report { ReporterId = "a", Target = ReportTarget.Reply, TargetId = 1, TargetUserId = "b", Status = ReportStatus.Removed };
            db.AddRange(r, rep);
            await db.SaveChangesAsync();
            db.Notifications.AddRange(
                new Notification { UserId = "a", ActorId = "b", Type = NotificationType.Reply, SubjectId = r.Id },
                new Notification { UserId = "a", ActorId = "b", Type = NotificationType.Reaction, SubjectId = review },
                new Notification { UserId = "a", ActorId = "a", Type = NotificationType.ReportOutcome, SubjectId = rep.Id },
                new Notification { UserId = "a", ActorId = "c", Type = NotificationType.FriendAccepted });
            await db.SaveChangesAsync();
        }

        using var db2 = open();
        var items = Json(await Bell(db2, "a").List(null)).GetProperty("items").EnumerateArray().ToDictionary(i => i.GetProperty("type").GetString()!);
        Assert.Equal("Title 2", items["Reply"].GetProperty("titleName").GetString());
        Assert.Equal(review, items["Reply"].GetProperty("reviewId").GetInt32());
        Assert.Equal("Title 2", items["Reaction"].GetProperty("titleName").GetString());
        Assert.Equal("Reply", items["ReportOutcome"].GetProperty("contentType").GetString());
        Assert.Equal("Removed", items["ReportOutcome"].GetProperty("outcome").GetString());
        Assert.Null(items["ReportOutcome"].GetProperty("titleName").GetString());
        Assert.Null(items["FriendAccepted"].GetProperty("titleName").GetString());
        Assert.Null(items["FriendAccepted"].GetProperty("contentType").GetString());
    }

    // a reply (or reaction) whose review is gone still lists, with no title or link, so the web falls back to "your review"
    [Fact]
    public async Task Gone_review_gives_null_title_and_link()
    {
        var open = Mem();
        await Seed(open);
        using (var db = open())
        {
            db.Notifications.AddRange(
                new Notification { UserId = "a", ActorId = "b", Type = NotificationType.Reply, SubjectId = 999 },
                new Notification { UserId = "a", ActorId = "c", Type = NotificationType.Reaction, SubjectId = 998 });
            await db.SaveChangesAsync();
        }
        using var db2 = open();
        var items = Json(await Bell(db2, "a").List(null)).GetProperty("items").EnumerateArray().ToDictionary(i => i.GetProperty("type").GetString()!);
        Assert.Null(items["Reply"].GetProperty("titleName").GetString());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, items["Reply"].GetProperty("reviewId").ValueKind);
        Assert.Null(items["Reaction"].GetProperty("titleName").GetString());
    }
}
