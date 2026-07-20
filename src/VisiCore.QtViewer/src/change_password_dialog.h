#pragma once

#include "app_dialog.h"

class ApiClient;
class QLabel;
class QLineEdit;
class QPushButton;

class ChangePasswordDialog final : public AppDialog {
    Q_OBJECT

public:
    explicit ChangePasswordDialog(
        ApiClient *apiClient,
        QWidget *parent = nullptr,
        bool passwordChangeRequired = false,
        const QString &currentPassword = {});

protected:
    void reject() override;

private slots:
    void submit();
    void passwordChangeFailed(const QString &message);

private:
    void setSubmitting(bool submitting);
    void showError(const QString &message);

    ApiClient *apiClient_;
    QLineEdit *currentPasswordEdit_;
    QLineEdit *newPasswordEdit_;
    QLineEdit *confirmPasswordEdit_;
    QLabel *errorLabel_;
    QPushButton *submitButton_;
    QPushButton *cancelButton_;
    bool submitting_ = false;
};
