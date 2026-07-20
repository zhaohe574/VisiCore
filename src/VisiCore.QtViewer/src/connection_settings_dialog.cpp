#include "connection_settings_dialog.h"

#include "icon_provider.h"

#include <QHBoxLayout>
#include <QLabel>
#include <QLineEdit>
#include <QNetworkAccessManager>
#include <QNetworkReply>
#include <QNetworkRequest>
#include <QPushButton>
#include <QStyle>
#include <QUrl>

ConnectionSettingsDialog::ConnectionSettingsDialog(
    QUrl currentBaseUrl,
    bool allowInsecureHttp,
    QWidget *parent)
    : AppDialog(QStringLiteral("中心连接设置"), parent), settings_(allowInsecureHttp) {
    setModal(true);
    setMinimumWidth(520);

    auto *description = new QLabel(
        allowInsecureHttp
            ? QStringLiteral("配置用于登录和播放的中心地址。仅允许 HTTPS，或本机回环地址的 HTTP。")
            : QStringLiteral("配置用于登录和播放的中心地址。生产环境仅允许 HTTPS。"),
        this);
    description->setWordWrap(true);
    description->setObjectName(QStringLiteral("loginSubtitle"));

    auto *label = new QLabel(QStringLiteral("中心地址"), this);
    label->setObjectName(QStringLiteral("panelTitle"));
    urlEdit_ = new QLineEdit(this);
    urlEdit_->setClearButtonEnabled(true);
    urlEdit_->setPlaceholderText(QStringLiteral("https://visicore.example/"));
    urlEdit_->setText(ConnectionSettings::normalize(std::move(currentBaseUrl)).toString(QUrl::FullyEncoded));
    urlEdit_->addAction(IconProvider::instance().icon(ViewerIcon::Settings, QSize(17, 17)), QLineEdit::LeadingPosition);

    feedbackLabel_ = new QLabel(this);
    feedbackLabel_->setObjectName(QStringLiteral("connectionSettingsFeedback"));
    feedbackLabel_->setWordWrap(true);
    feedbackLabel_->hide();

    testButton_ = new QPushButton(QStringLiteral("测试连接"), this);
    saveButton_ = new QPushButton(QStringLiteral("保存并使用"), this);
    saveButton_->setProperty("primary", true);
    auto *cancelButton = new QPushButton(QStringLiteral("取消"), this);
    auto *buttons = new QHBoxLayout;
    buttons->setContentsMargins(0, 0, 0, 0);
    buttons->addWidget(testButton_);
    buttons->addStretch(1);
    buttons->addWidget(cancelButton);
    buttons->addWidget(saveButton_);

    contentLayout()->addWidget(description);
    contentLayout()->addSpacing(8);
    contentLayout()->addWidget(label);
    contentLayout()->addWidget(urlEdit_);
    contentLayout()->addWidget(feedbackLabel_);
    contentLayout()->addSpacing(4);
    contentLayout()->addLayout(buttons);

    network_ = new QNetworkAccessManager(this);
    connect(testButton_, &QPushButton::clicked, this, &ConnectionSettingsDialog::testConnection);
    connect(saveButton_, &QPushButton::clicked, this, &ConnectionSettingsDialog::saveConnection);
    connect(cancelButton, &QPushButton::clicked, this, &QDialog::reject);
}

QUrl ConnectionSettingsDialog::baseUrl() const {
    return ConnectionSettings::normalize(QUrl(urlEdit_->text().trimmed(), QUrl::StrictMode));
}

void ConnectionSettingsDialog::testConnection() {
    QString errorMessage;
    const QUrl url = baseUrl();
    if (!settings_.isAllowed(url, &errorMessage)) {
        setFeedback(errorMessage, true);
        return;
    }
    testButton_->setEnabled(false);
    testButton_->setText(QStringLiteral("正在测试…"));
    setFeedback(QStringLiteral("正在连接中心健康检查接口…"), false);
    QNetworkRequest request(url.resolved(QUrl(QStringLiteral("healthz"))));
    request.setTransferTimeout(5000);
    request.setRawHeader("Accept", "application/json");
    auto *reply = network_->get(request);
    connect(reply, &QNetworkReply::finished, this, [this, reply]() {
        testButton_->setEnabled(true);
        testButton_->setText(QStringLiteral("测试连接"));
        const int statusCode = reply->attribute(QNetworkRequest::HttpStatusCodeAttribute).toInt();
        if (reply->error() == QNetworkReply::NoError && statusCode >= 200 && statusCode < 300) {
            setFeedback(QStringLiteral("中心连接正常。"), false);
        } else {
            setFeedback(QStringLiteral("无法连接中心，请检查地址、证书和网络后重试。"), true);
        }
        reply->deleteLater();
    });
}

void ConnectionSettingsDialog::saveConnection() {
    QString errorMessage;
    if (!settings_.save(baseUrl(), &errorMessage)) {
        setFeedback(errorMessage, true);
        return;
    }
    accept();
}

void ConnectionSettingsDialog::setFeedback(const QString &message, bool isError) {
    feedbackLabel_->setProperty("error", isError);
    feedbackLabel_->setText(message);
    feedbackLabel_->show();
    feedbackLabel_->style()->unpolish(feedbackLabel_);
    feedbackLabel_->style()->polish(feedbackLabel_);
}
