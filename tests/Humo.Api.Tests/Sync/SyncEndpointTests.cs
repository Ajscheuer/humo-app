using System.Net;
using System.Net.Http.Json;
using Humo.Api.Tests.Support;
using Humo.Shared.Entities;
using Humo.Shared.Enums;
using Humo.Shared.Sync;

namespace Humo.Api.Tests.Sync;

/// <summary>
/// The endpoints over real HTTP: status codes, auth, and the account boundary as
/// a client actually meets it.
/// </summary>
public class SyncEndpointTests : IClassFixture<SyncApiFactory>
{
    private const string Alice = "alice@example.com";
    private const string Bob = "bob@example.com";

    private readonly SyncApiFactory _factory;

    public SyncEndpointTests(SyncApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Push_without_a_token_is_refused()
    {
        var response = await _factory.AnonymousClient()
            .PostAsJsonAsync("/sync/push", Batch());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Pull_without_a_token_is_refused()
    {
        var response = await _factory.AnonymousClient()
            .GetAsync($"/sync/pull?deviceId={Guid.NewGuid()}&cursor=0");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task A_push_without_a_device_id_is_a_bad_request()
    {
        var response = await _factory.ClientFor(Alice)
            .PostAsJsonAsync("/sync/push", Batch(device: Guid.Empty));

        // Cursors are per device. Accepting this would leave the caller with
        // nowhere to record how far it had read.
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task A_pull_with_a_negative_cursor_is_a_bad_request()
    {
        var response = await _factory.ClientFor(Alice)
            .GetAsync($"/sync/pull?deviceId={Guid.NewGuid()}&cursor=-1");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task A_pull_without_a_device_id_is_a_bad_request()
    {
        var response = await _factory.ClientFor(Alice)
            .GetAsync($"/sync/pull?deviceId={Guid.Empty}&cursor=0");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task A_round_trip_returns_the_record_to_the_accounts_other_device()
    {
        var client = _factory.ClientFor(Alice);
        var phone = Guid.NewGuid();
        var tablet = Guid.NewGuid();
        var rig = ARig("Round trip rig");

        var push = await client.PostAsJsonAsync("/sync/push", Batch(phone, rig));
        push.EnsureSuccessStatusCode();

        var pulled = await client.GetFromJsonAsync<SyncPullResponse>(
            $"/sync/pull?deviceId={tablet}&cursor=0");

        Assert.NotNull(pulled);
        Assert.Contains(pulled.Equipment, e => e.Id == rig.Id);
    }

    [Fact]
    public async Task A_batch_with_a_rejection_is_still_a_200()
    {
        var orphan = new Cook
        {
            EquipmentId = Guid.NewGuid(),
            PitType = EquipmentType.Offset,
            MeatType = MeatType.Brisket,
            WeightKg = 6,
            StartedAt = DateTimeOffset.UtcNow,
            LastActivityAt = DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };

        var response = await _factory.ClientFor(Alice).PostAsJsonAsync(
            "/sync/push",
            new SyncPushRequest { DeviceId = Guid.NewGuid(), Cooks = [orphan] });

        // A 4xx would tell the client to stop. What it must do is re-send a
        // subset, and the body says which.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<SyncPushResponse>();

        Assert.NotNull(body);
        Assert.True(body.HasRejections);
        Assert.True(Assert.Single(body.Rejected).IsRetryable);
    }

    [Fact]
    public async Task One_signed_in_user_cannot_pull_anothers_records()
    {
        var rig = ARig("Alice's rig");
        var push = await _factory.ClientFor(Alice)
            .PostAsJsonAsync("/sync/push", Batch(Guid.NewGuid(), rig));
        push.EnsureSuccessStatusCode();

        var bobsPull = await _factory.ClientFor(Bob)
            .GetFromJsonAsync<SyncPullResponse>($"/sync/pull?deviceId={Guid.NewGuid()}&cursor=0");

        Assert.NotNull(bobsPull);
        Assert.DoesNotContain(bobsPull.Equipment, e => e.Id == rig.Id);
    }

    [Fact]
    public async Task An_account_id_in_the_body_does_not_move_a_record_to_that_account()
    {
        var rig = ARig("Smuggled");

        // Alice naming an arbitrary account in the payload. It lands in hers.
        rig.AccountId = Guid.NewGuid();

        var push = await _factory.ClientFor(Alice)
            .PostAsJsonAsync("/sync/push", Batch(Guid.NewGuid(), rig));
        push.EnsureSuccessStatusCode();

        var back = await _factory.ClientFor(Alice)
            .GetFromJsonAsync<SyncPullResponse>($"/sync/pull?deviceId={Guid.NewGuid()}&cursor=0");

        Assert.NotNull(back);
        Assert.Contains(back.Equipment, e => e.Id == rig.Id);
    }

    [Fact]
    public async Task The_same_subject_signing_in_twice_lands_on_the_same_account()
    {
        var rig = ARig("Same person, new phone");
        var push = await _factory.ClientFor(Alice)
            .PostAsJsonAsync("/sync/push", Batch(Guid.NewGuid(), rig));
        push.EnsureSuccessStatusCode();

        // A fresh client, same subject: a reinstall, or the second device.
        var pull = await _factory.ClientFor(Alice)
            .GetFromJsonAsync<SyncPullResponse>($"/sync/pull?deviceId={Guid.NewGuid()}&cursor=0");

        Assert.NotNull(pull);
        Assert.Contains(pull.Equipment, e => e.Id == rig.Id);
    }

    private static SyncPushRequest Batch(Guid? device = null, params Equipment[] equipment)
        => new() { DeviceId = device ?? Guid.NewGuid(), Equipment = equipment };

    private static Equipment ARig(string name) => new()
    {
        Name = name,
        Type = EquipmentType.Offset,
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow,
    };
}
