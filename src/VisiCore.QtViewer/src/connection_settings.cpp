#include "connection_settings.h"

#include <QSettings>

namespace {
const auto BaseUrlKey = QStringLiteral("viewerConnection/baseUrl");

bool isLoopbackHost(const QString &host) {
    const QString normalized = host.trimmed().toLower();
    return normalized == QStringLiteral("127.0.0.1") || normalized == QStringLiteral("localhost") ||
           normalized == QStringLiteral("::1");
}
}

ConnectionSettings::ConnectionSettings(bool allowInsecureHttp)
    : allowInsecureHttp_(allowInsecureHttp) {
}

QUrl ConnectionSettings::savedBaseUrl() const {
    const QUrl configured = normalize(QUrl(QSettings().value(BaseUrlKey).toString()));
    return isAllowed(configured) ? configured : defaultBaseUrl();
}

bool ConnectionSettings::isAllowed(const QUrl &url, QString *errorMessage) const {
    const QUrl normalized = normalize(url);
    if (!normalized.isValid() || normalized.host().isEmpty() || !normalized.userInfo().isEmpty()) {
        if (errorMessage != nullptr) {
            *errorMessage = QStringLiteral("请输入不含用户名、密码的完整中心地址。");
        }
        return false;
    }
    if (!normalized.query().isEmpty() || !normalized.fragment().isEmpty()) {
        if (errorMessage != nullptr) {
            *errorMessage = QStringLiteral("中心地址不能包含查询参数或片段。");
        }
        return false;
    }
    if (normalized.scheme().compare(QStringLiteral("https"), Qt::CaseInsensitive) == 0) {
        return true;
    }
    if (allowInsecureHttp_ && normalized.scheme().compare(QStringLiteral("http"), Qt::CaseInsensitive) == 0 &&
        isLoopbackHost(normalized.host())) {
        return true;
    }
    if (errorMessage != nullptr) {
        *errorMessage = allowInsecureHttp_
            ? QStringLiteral("仅允许 HTTPS，或本机回环地址的 HTTP。")
            : QStringLiteral("仅允许 HTTPS 中心地址。");
    }
    return false;
}

bool ConnectionSettings::save(const QUrl &url, QString *errorMessage) const {
    if (!isAllowed(url, errorMessage)) {
        return false;
    }
    QSettings settings;
    settings.setValue(BaseUrlKey, normalize(url).toString(QUrl::FullyEncoded));
    settings.sync();
    if (settings.status() != QSettings::NoError) {
        if (errorMessage != nullptr) {
            *errorMessage = QStringLiteral("无法保存中心地址，请检查当前 Windows 用户的应用数据目录权限。");
        }
        return false;
    }
    return true;
}

QUrl ConnectionSettings::normalize(QUrl url) {
    if (!url.path().endsWith(u'/')) {
        url.setPath(url.path() + u'/');
    }
    return url;
}

QUrl ConnectionSettings::defaultBaseUrl() {
    return QUrl(QStringLiteral("https://visicore.local/"));
}
