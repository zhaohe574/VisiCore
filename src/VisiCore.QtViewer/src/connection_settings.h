#pragma once

#include <QUrl>

class ConnectionSettings final {
public:
    explicit ConnectionSettings(bool allowInsecureHttp);

    [[nodiscard]] QUrl savedBaseUrl() const;
    [[nodiscard]] bool isAllowed(const QUrl &url, QString *errorMessage = nullptr) const;
    bool save(const QUrl &url, QString *errorMessage = nullptr) const;

    static QUrl normalize(QUrl url);
    static QUrl defaultBaseUrl();

private:
    bool allowInsecureHttp_ = false;
};
