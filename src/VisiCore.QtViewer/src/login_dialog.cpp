#include "login_dialog.h"

#include "apiclient.h"
#include "app_dialog.h"
#include "change_password_dialog.h"
#include "connection_settings_dialog.h"
#include "icon_provider.h"
#include "theme_manager.h"

#include <QCheckBox>
#include <QHBoxLayout>
#include <QLabel>
#include <QLineEdit>
#include <QPushButton>
#include <QSettings>
#include <QVBoxLayout>

namespace {
const auto rememberedUsernameKey = QStringLiteral("viewerLogin/rememberedUsername");
}

LoginDialog::LoginDialog(ApiClient *apiClient, QWidget *parent)
    : AppDialog(QStringLiteral("登录视枢"), parent), apiClient_(apiClient) {
    setModal(true);
    setMinimumWidth(450);
    resize(470, 480);

    auto *brandMark = new QWidget(this);
    brandMark->setObjectName(QStringLiteral("loginBrandMark"));
    brandMark->setFixedSize(46, 46);
    auto *brandMarkLayout = new QHBoxLayout(brandMark);
    brandMarkLayout->setContentsMargins(10, 10, 10, 10);
    auto *brandIcon = new QLabel(brandMark);
    brandIcon->setPixmap(IconProvider::instance()
                             .icon(ViewerIcon::Camera, ThemeManager::instance().color(ThemeColor::PrimaryHover), QSize(24, 24))
                             .pixmap(QSize(24, 24)));
    brandMarkLayout->addWidget(brandIcon);

    auto *eyebrow = new QLabel(QStringLiteral("视枢"), this);
    eyebrow->setObjectName(QStringLiteral("loginEyebrow"));
    auto *title = new QLabel(QStringLiteral("登录监控工作台"), this);
    title->setObjectName(QStringLiteral("loginTitle"));
    auto *subtitle = new QLabel(QStringLiteral("使用已授权账号继续"), this);
    subtitle->setObjectName(QStringLiteral("loginSubtitle"));
    auto *brandTextLayout = new QVBoxLayout;
    brandTextLayout->setContentsMargins(0, 0, 0, 0);
    brandTextLayout->setSpacing(2);
    brandTextLayout->addWidget(eyebrow);
    brandTextLayout->addWidget(title);
    brandTextLayout->addWidget(subtitle);
    auto *brandLayout = new QHBoxLayout;
    brandLayout->setContentsMargins(0, 0, 0, 0);
    brandLayout->setSpacing(13);
    brandLayout->addWidget(brandMark, 0, Qt::AlignTop);
    brandLayout->addLayout(brandTextLayout, 1);

    usernameEdit_ = new QLineEdit(this);
    usernameEdit_->setPlaceholderText(QStringLiteral("请输入用户名"));
    usernameEdit_->setClearButtonEnabled(true);
    usernameEdit_->addAction(IconProvider::instance().icon(ViewerIcon::User, QSize(17, 17)), QLineEdit::LeadingPosition);
    usernameEdit_->setText(QSettings().value(rememberedUsernameKey).toString());
    passwordEdit_ = new QLineEdit(this);
    passwordEdit_->setPlaceholderText(QStringLiteral("请输入密码"));
    passwordEdit_->addAction(IconProvider::instance().icon(ViewerIcon::Password, QSize(17, 17)), QLineEdit::LeadingPosition);
    passwordEdit_->setEchoMode(QLineEdit::Password);
    passwordEdit_->setInputMethodHints(Qt::ImhHiddenText | Qt::ImhSensitiveData | Qt::ImhNoPredictiveText);
    rememberUsernameCheck_ = new QCheckBox(QStringLiteral("记住账号"), this);
    rememberUsernameCheck_->setChecked(!usernameEdit_->text().isEmpty());
    errorLabel_ = new QLabel(this);
    errorLabel_->setObjectName(QStringLiteral("errorLabel"));
    errorLabel_->setWordWrap(true);
    errorLabel_->hide();
    loginButton_ = new QPushButton(QStringLiteral("登录"), this);
    loginButton_->setObjectName(QStringLiteral("loginButton"));
    loginButton_->setProperty("primary", true);
    loginButton_->setMinimumHeight(38);
    loginButton_->setDefault(true);
    auto *connectionButton = new QPushButton(QStringLiteral("中心连接设置"), this);
    connectionButton->setObjectName(QStringLiteral("connectionSettingsButton"));

    auto *usernameLabel = new QLabel(QStringLiteral("用户名"), this);
    usernameLabel->setObjectName(QStringLiteral("panelTitle"));
    auto *passwordLabel = new QLabel(QStringLiteral("密码"), this);
    passwordLabel->setObjectName(QStringLiteral("panelTitle"));

    contentLayout()->setSpacing(12);
    contentLayout()->addLayout(brandLayout);
    contentLayout()->addSpacing(12);
    contentLayout()->addWidget(usernameLabel);
    contentLayout()->addWidget(usernameEdit_);
    contentLayout()->addWidget(passwordLabel);
    contentLayout()->addWidget(passwordEdit_);
    contentLayout()->addWidget(rememberUsernameCheck_);
    contentLayout()->addWidget(connectionButton, 0, Qt::AlignLeft);
    contentLayout()->addWidget(errorLabel_);
    contentLayout()->addSpacing(2);
    contentLayout()->addWidget(loginButton_);

    connect(loginButton_, &QPushButton::clicked, this, &LoginDialog::submit);
    connect(passwordEdit_, &QLineEdit::returnPressed, this, &LoginDialog::submit);
    connect(connectionButton, &QPushButton::clicked, this, [this]() {
        ConnectionSettingsDialog dialog(apiClient_->baseUrl(), apiClient_->allowsInsecureHttp(), this);
        if (dialog.exec() != QDialog::Accepted) {
            return;
        }
        if (!apiClient_->setBaseUrl(dialog.baseUrl())) {
            loginFailed(QStringLiteral("中心地址无效，请检查协议和主机名后重试。"));
            return;
        }
        errorLabel_->hide();
        passwordEdit_->clear();
        passwordEdit_->setFocus();
    });
    connect(apiClient_, &ApiClient::loginSucceeded, this, [this](const QString &username) {
        QSettings settings;
        if (rememberUsernameCheck_->isChecked()) {
            settings.setValue(rememberedUsernameKey, username);
        } else {
            settings.remove(rememberedUsernameKey);
        }
        if (apiClient_->passwordChangeRequired()) {
            const QString currentPassword = passwordEdit_->text();
            passwordEdit_->clear();
            ChangePasswordDialog changePasswordDialog(apiClient_, this, true, currentPassword);
            if (changePasswordDialog.exec() == QDialog::Accepted) {
                AppDialog::success(
                    this,
                    QStringLiteral("密码修改成功"),
                    QStringLiteral("请使用新密码重新登录。"));
            } else {
                apiClient_->logout();
            }
            loginButton_->setEnabled(true);
            loginButton_->setText(QStringLiteral("登录"));
            passwordEdit_->setFocus();
            return;
        }

        passwordEdit_->clear();
        accept();
    });
    connect(apiClient_, &ApiClient::loginFailed, this, &LoginDialog::loginFailed);

    if (usernameEdit_->text().isEmpty()) {
        usernameEdit_->setFocus();
    } else {
        passwordEdit_->setFocus();
    }
}

void LoginDialog::submit() {
    const QString username = usernameEdit_->text().trimmed();
    const QString password = passwordEdit_->text();
    if (username.isEmpty() || password.isEmpty()) {
        loginFailed(QStringLiteral("请输入用户名和密码。"));
        return;
    }
    errorLabel_->hide();
    loginButton_->setEnabled(false);
    loginButton_->setText(QStringLiteral("正在验证…"));
    apiClient_->login(username, password);
}

void LoginDialog::loginFailed(const QString &message) {
    errorLabel_->setText(message);
    errorLabel_->show();
    loginButton_->setEnabled(true);
    loginButton_->setText(QStringLiteral("登录"));
    passwordEdit_->selectAll();
    passwordEdit_->setFocus();
}
