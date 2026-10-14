using Microsoft.AspNetCore.Mvc;
using VideoPlatform.Contracts;
using VideoPlatform.Domain;
using VideoPlatform.Infrastructure;
using VideoPlatform.Infrastructure.Pon;

namespace VideoPlatform.Api.Modules;

public static class PonEndpoints
{
    public static void MapPonEndpoints(this WebApplication app)
    {
        var ponGroup = app.MapGroup("/api/v2/pon").RequireAuthorization().WithTags("光网与ONU管理").AddEndpointFilter<AssetOperationFilter>();

        // 1. 获取光网统计信息（总数、在线数、离线数、型号分布）
        ponGroup.MapGet("/statistics", async (HttpContext context, AccessService access, PonService pon) =>
        {
            await access.DemandAsync(ApiSupport.Actor(context), "pon.read");
            return Results.Ok(await pon.GetStatisticsAsync(context.RequestAborted, ApiSupport.Actor(context)));
        }).Produces<PonStatisticsDto>().WithName("GetPonStatistics");

        // 2. 分页查询 ONU 资产列表
        ponGroup.MapGet("/onus", async (
            HttpContext context,
            AccessService access,
            PonService pon,
            [FromQuery] string? search,
            [FromQuery] string? status,
            [FromQuery] long? terminalBoxId,
            [FromQuery] int? page,
            [FromQuery] int? pageSize,
            [FromQuery] int? size) =>
        {
            await access.DemandAsync(ApiSupport.Actor(context), "pon.read");
            var effectivePage = page ?? 1;
            var effectiveSize = pageSize ?? size ?? 20;
            var result = await pon.ListOnusAsync(search, status, terminalBoxId, effectivePage, effectiveSize, context.RequestAborted, ApiSupport.Actor(context));
            return Results.Ok(result);
        }).Produces<PagedResponse<OnuDeviceDto>>().WithName("ListOnus");

        // 3. 查询单个 ONU 详情
        ponGroup.MapGet("/onus/{id:long}", async (long id, HttpContext context, AccessService access, PonService pon) =>
        {
            await access.DemandAsync(ApiSupport.Actor(context), "pon.read");
            return Results.Ok(await pon.GetOnuAsync(id, context.RequestAborted));
        }).Produces<OnuDeviceDto>().WithName("GetOnu");

        // 4. 更新 ONU 本地配置（别名、终端箱、下挂交换机等）并同步网管
        ponGroup.MapPut("/onus/{id:long}", async (long id, OnuUpdateRequest request, HttpContext context, AccessService access, PonService pon, AuditStore audit) =>
        {
            var actor = ApiSupport.Actor(context);
            await access.DemandAsync(actor, "pon.manage");
            var updated = await pon.UpdateOnuAsync(id, request, context.RequestAborted);
            await audit.WriteAsync(actor.UserId, "onu.update", id.ToString(), $"更新 ONU 配置：{updated.OnuName} (别名：{request.Alias})", ApiSupport.Ip(context));
            return Results.Ok(updated);
        }).Produces<OnuDeviceDto>().WithName("UpdateOnu");

        // 5. 实时光衰与端口诊断
        ponGroup.MapGet("/onus/{id:long}/diagnosis", async (long id, HttpContext context, AccessService access, PonService pon) =>
        {
            await access.DemandAsync(ApiSupport.Actor(context), "pon.read");
            return Results.Ok(await pon.DiagnoseOnuAsync(id, context.RequestAborted));
        }).Produces<OnuDiagnosisDto>().WithName("DiagnoseOnu");

        // 6. 远程免开箱重启 ONU
        ponGroup.MapPost("/onus/{id:long}/reboot", async (long id, HttpContext context, AccessService access, PonService pon, AuditStore audit) =>
        {
            var actor = ApiSupport.Actor(context);
            await access.DemandAsync(actor, "pon.manage");
            await pon.RebootOnuAsync(id, context.RequestAborted);
            await audit.WriteAsync(actor.UserId, "onu.reboot", id.ToString(), $"远程重启 ONU 设备：{id}", ApiSupport.Ip(context));
            return Results.Ok(new PonRebootResponse(true, "重启指令已成功下发至网管"));
        }).Produces<PonRebootResponse>().WithName("RebootOnu");

        // 7. 手动触发从华为 eSight 网管全量同步
        ponGroup.MapPost("/sync", async (HttpContext context, AccessService access, PonService pon, AuditStore audit, Database db) =>
        {
            var actor = ApiSupport.Actor(context);
            await access.DemandAsync(actor, "pon.manage");
            Rules.Require(await AssetScope.IsFullAsync(db, actor, context.RequestAborted), "全网同步需要完整数据范围", "asset.scope", 403);
            var count = await pon.SyncFromEsightAsync(context.RequestAborted);
            await audit.WriteAsync(actor.UserId, "pon.sync", "all", $"全量同步网管 ONU 数据，共入库/更新 {count} 台", ApiSupport.Ip(context));
            return Results.Ok(new PonSyncResponse(true, count));
        }).Produces<PonSyncResponse>().WithName("SyncPonFromEsight");

        // 8. 终端箱向下维度的综合运维看板（终端箱 -> ONU -> 摄像头）
        ponGroup.MapGet("/dashboard/kanban", async (HttpContext context, AccessService access, PonService pon) =>
        {
            await access.DemandAsync(ApiSupport.Actor(context), "pon.read");
            return Results.Ok(await pon.GetKanbanDashboardAsync(context.RequestAborted, ApiSupport.Actor(context)));
        }).Produces<PonKanbanResponseDto>().WithName("GetPonKanbanDashboard");

        // 9. 设备掉线与恢复历史记录查询
        ponGroup.MapGet("/dashboard/history", async (
            HttpContext context,
            AccessService access,
            PonService pon,
            [FromQuery] long? terminalBoxId,
            [FromQuery] long? onuDeviceId,
            [FromQuery] long? channelId,
            [FromQuery] string? deviceType,
            [FromQuery] string? status,
            [FromQuery] string? search,
            [FromQuery] DateTimeOffset? from,
            [FromQuery] DateTimeOffset? to,
            [FromQuery] int? page,
            [FromQuery] int? pageSize,
            [FromQuery] int? size) =>
        {
            await access.DemandAsync(ApiSupport.Actor(context), "pon.read");
            var effectivePage = page ?? 1;
            var effectiveSize = pageSize ?? size ?? 20;
            var result = await pon.GetOfflineHistoryAsync(terminalBoxId, onuDeviceId, channelId, deviceType, status, search, from, to, effectivePage, effectiveSize, context.RequestAborted, ApiSupport.Actor(context));
            return Results.Ok(result);
        }).Produces<PagedResponse<DeviceOfflineHistoryDto>>().WithName("GetDeviceOfflineHistory");

        // 10. 查询 OLT 核心网元与 8 个 PON 口拓扑指标
        ponGroup.MapGet("/olts", async (HttpContext context, AccessService access, PonService pon) =>
        {
            await access.DemandAsync(ApiSupport.Actor(context), "pon.read");
            return Results.Ok(await pon.GetOltsWithPortsAsync(context.RequestAborted, ApiSupport.Actor(context)));
        }).Produces<IReadOnlyList<OltDeviceDto>>().WithName("GetOltsWithPorts");

        // 11. 全网光链路健康指数与亚健康趋势大屏
        ponGroup.MapGet("/health-index", async (HttpContext context, AccessService access, PonService pon) =>
        {
            await access.DemandAsync(ApiSupport.Actor(context), "pon.read");
            return Results.Ok(await pon.GetHealthIndexAsync(context.RequestAborted, ApiSupport.Actor(context)));
        }).Produces<OpticalHealthIndexDto>().WithName("GetOpticalHealthIndex");

        // 12. 基于 MAC/IP/命名特征的通道自动拓扑发现与纠偏推荐
        ponGroup.MapPost("/bindings/auto-discover", async (HttpContext context, AccessService access, PonService pon) =>
        {
            await access.DemandAsync(ApiSupport.Actor(context), "pon.read");
            return Results.Ok(await pon.AutoDiscoverBindingsAsync(context.RequestAborted, ApiSupport.Actor(context)));
        }).Produces<AutoTopologyDiscoverResultDto>().WithName("AutoDiscoverTopologyBindings");

        // 13. 批量应用拓扑纠偏建议
        ponGroup.MapPost("/bindings/apply", async (ApplyTopologyCorrectionRequest request, HttpContext context, AccessService access, PonService pon, AuditStore audit) =>
        {
            var actor = ApiSupport.Actor(context);
            await access.DemandAsync(actor, "pon.manage");
            var appliedCount = await pon.ApplyTopologyCorrectionsAsync(request, context.RequestAborted);
            await audit.WriteAsync(actor.UserId, "pon.apply_bindings", "all", $"批量应用 {appliedCount} 条光链路拓扑绑定纠偏", ApiSupport.Ip(context));
            return Results.Ok(new TopologyCorrectionResponse(true, appliedCount));
        }).Produces<TopologyCorrectionResponse>().WithName("ApplyTopologyCorrections");



        var boxGroup = app.MapGroup("/api/v2/terminal-boxes").RequireAuthorization().WithTags("终端箱与网络拓扑台账").AddEndpointFilter<AssetOperationFilter>();

        // 1. 获取终端箱列表
        boxGroup.MapGet("", async (HttpContext context, AccessService access, TerminalBoxService boxes) =>
        {
            await access.DemandAsync(ApiSupport.Actor(context), "pon.read");
            return Results.Ok(await boxes.ListAsync(context.RequestAborted, ApiSupport.Actor(context)));
        }).Produces<IReadOnlyList<TerminalBoxDto>>().WithName("ListTerminalBoxes");

        // 2. 获取单个终端箱
        boxGroup.MapGet("/{id:long}", async (long id, HttpContext context, AccessService access, TerminalBoxService boxes) =>
        {
            await access.DemandAsync(ApiSupport.Actor(context), "pon.read");
            return Results.Ok(await boxes.GetAsync(id, context.RequestAborted));
        }).Produces<TerminalBoxDto>().WithName("GetTerminalBox");

        // 3. 创建终端箱
        boxGroup.MapPost("", async (TerminalBoxRequest request, HttpContext context, AccessService access, TerminalBoxService boxes, AuditStore audit) =>
        {
            var actor = ApiSupport.Actor(context);
            await access.DemandAsync(actor, "pon.manage");
            var created = await boxes.CreateAsync(request, context.RequestAborted);
            await audit.WriteAsync(actor.UserId, "terminal_box.create", created.Id.ToString(), $"创建网络终端箱：{created.Name} ({created.Code})", ApiSupport.Ip(context));
            return Results.Created($"/api/v2/terminal-boxes/{created.Id}", created);
        }).Produces<TerminalBoxDto>(201).WithName("CreateTerminalBox");

        // 4. 更新终端箱
        boxGroup.MapPut("/{id:long}", async (long id, TerminalBoxRequest request, HttpContext context, AccessService access, TerminalBoxService boxes, AuditStore audit) =>
        {
            var actor = ApiSupport.Actor(context);
            await access.DemandAsync(actor, "pon.manage");
            var updated = await boxes.UpdateAsync(id, request, context.RequestAborted);
            await audit.WriteAsync(actor.UserId, "terminal_box.update", id.ToString(), $"更新网络终端箱：{updated.Name} ({updated.Code})", ApiSupport.Ip(context));
            return Results.Ok(updated);
        }).Produces<TerminalBoxDto>().WithName("UpdateTerminalBox");

        // 5. 删除终端箱
        boxGroup.MapDelete("/{id:long}", async (long id, HttpContext context, AccessService access, TerminalBoxService boxes, AuditStore audit) =>
        {
            var actor = ApiSupport.Actor(context);
            await access.DemandAsync(actor, "pon.manage");
            await boxes.DeleteAsync(id, context.RequestAborted);
            await audit.WriteAsync(actor.UserId, "terminal_box.delete", id.ToString(), $"删除网络终端箱：{id}", ApiSupport.Ip(context));
            return Results.NoContent();
        }).Produces(204).WithName("DeleteTerminalBox");

        // 6. 获取【终端箱 -> ONU/交换机 -> 端口 -> 摄像头通道】完整拓扑树
        boxGroup.MapGet("/topology-tree", async (HttpContext context, AccessService access, TerminalBoxService boxes) =>
        {
            await access.DemandAsync(ApiSupport.Actor(context), "pon.read");
            return Results.Ok(await boxes.GetTopologyTreeAsync(context.RequestAborted, ApiSupport.Actor(context)));
        }).Produces<IReadOnlyList<TerminalBoxTopologyDto>>().WithName("GetTerminalBoxTopologyTree");

        // 7. 获取单个终端箱的完整内部拓扑树（ONU、光衰、挂接摄像头、工位机）
        boxGroup.MapGet("/{id:long}/topology", async (long id, HttpContext context, AccessService access, TerminalBoxService boxes) =>
        {
            await access.DemandAsync(ApiSupport.Actor(context), "pon.read");
            var top = await boxes.GetTopologyByIdAsync(id, context.RequestAborted, ApiSupport.Actor(context));
            return top is null ? Results.NotFound() : Results.Ok(top);
        }).Produces<TerminalBoxTopologyDto>().WithName("GetTerminalBoxTopology");
    }
}

