#pragma once

#include "app_dialog.h"

class ApiClient;
class QCheckBox;
class QLabel;
class QLineEdit;
class QPushButton;

class LoginDialog final : public AppDialog {
    Q_OBJECT

public:
    explicit LoginDialog(ApiClient *apiClient, QWidget *parent = nullptr);

private slots:
    void submit();
    void loginFailed(const QString &message);

private:
    ApiClient *apiClient_;
    QLineEdit *usernameEdit_;
    QLineEdit *passwordEdit_;
    QCheckBox *rememberUsernameCheck_;
    QLabel *errorLabel_;
    QPushButton *loginButton_;
};
