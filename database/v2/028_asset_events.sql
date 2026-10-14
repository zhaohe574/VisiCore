-- 资产通知随业务事务提交，前端只收到类型与编号，再按权限查询详情。
create or replace function notify_asset_change() returns trigger language plpgsql as $$
declare row_id bigint;
begin
    if TG_OP = 'UPDATE' and OLD is not distinct from NEW then return NEW; end if;
    if TG_OP = 'DELETE' then row_id := OLD.id; else row_id := NEW.id; end if;
    insert into outbox(kind, resource_id) values('asset.changed', TG_ARGV[0] || ':' || row_id::text);
    if TG_OP = 'DELETE' then return OLD; else return NEW; end if;
end;
$$;

do $$
declare entry text[];
begin
    foreach entry slice 1 in array ARRAY[
        ['channels','channel'],['devices','device'],['computers','computer'],
        ['terminal_boxes','terminal-box'],['onu_devices','onu'],['network_devices','network-device'],
        ['virtual_machines','virtual-machine'],['ip_pools','ip-pool'],
        ['device_ports','port'],['device_links','link'],
        ['workshops','organization'],['areas','organization'],['units','organization']
    ] loop
        execute format('create trigger asset_change after insert or update or delete on %I for each row execute function notify_asset_change(%L)', entry[1], entry[2]);
    end loop;
end;
$$;

create or replace function notify_channel_binding_change() returns trigger language plpgsql as $$
declare row_id bigint;
begin
    if TG_OP = 'UPDATE' and OLD is not distinct from NEW then return NEW; end if;
    if TG_OP = 'DELETE' then row_id := OLD.channel_id; else row_id := NEW.channel_id; end if;
    insert into outbox(kind, resource_id) values('asset.changed', 'channel:' || row_id::text);
    if TG_OP = 'DELETE' then return OLD; else return NEW; end if;
end;
$$;
create trigger asset_binding_change after insert or update or delete on channel_network_bindings
    for each row execute function notify_channel_binding_change();
