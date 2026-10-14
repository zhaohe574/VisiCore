-- 播放格位独立拥有媒体租约，旧客户端继续使用可空字段。
alter table media_sessions add column if not exists consumer_id uuid;
create index if not exists ix_media_consumer_active
    on media_sessions(auth_session_id, consumer_id, kind)
    where closed_at is null;
