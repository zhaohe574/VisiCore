#pragma once

#include "connection_settings.h"

#include "app_dialog.h"

class QLineEdit;
class QLabel;
class QPushButton;
class QNetworkAccessManager;

class ConnectionSettingsDialog final : public AppDialog {
    Q_OBJECT

public:
    explicit ConnectionSettingsDialog(QUrl currentBaseUrl, bool allowInsecureHttp, QWidget *parent = nullptr);

    [[nodiscard]] QUrl baseUrl() const;

private:
    void testConnection();
    void saveConnection();
    void setFeedback(const QString &message, bool isError);

    ConnectionSettings settings_;
    QLineEdit *urlEdit_ = nullptr;
    QLabel *feedbackLabel_ = nullptr;
    QPushButton *testButton_ = nullptr;
    QPushButton *saveButton_ = nullptr;
    QNetworkAccessManager *network_ = nullptr;
};
