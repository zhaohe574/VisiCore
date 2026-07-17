#include "change_password_dialog.h"

#include "apiclient.h"
#include "icon_provider.h"

#include <QDialogButtonBox>
#include <QFormLayout>
#include <QLabel>
#include <QLineEdit>
#include <QPushButton>
#include <QVBoxLayout>

ChangePasswordDialog::ChangePasswordDialog(
    ApiClient *apiClient,
    QWidget *parent,
    bool passwordChangeRequired,
    const QString &currentPassword)
    : AppDialog(passwordChangeRequired ? QStringLiteral("首次登录必须修改密码") : QStringLiteral("修改密码"), parent),
      apiClient_(apiClient) {
    setModal(true);
    setMinimumWidth(470);
    resize(500, 510);

    auto *title = new QLabel(passwordChangeRequired ? QStringLiteral("必须修改初始密码") : QStringLiteral("修改当前账号密码"));
    title->setObjectName(QStringLiteral("loginTitle"));
    auto *subtitle = new QLabel(passwordChangeRequired
                                    ? QStringLiteral("首次登录或密码重置后，必须设置新密码才能进入系统。")
                                    : QStringLiteral("修改成功后需要使用新密码重新登录。"));
    subtitle->setObjectName(QStringLiteral("loginSubtitle"));

    currentPasswordEdit_ = new QLineEdit(this);
    currentPasswordEdit_->setPlaceholderText(QStringLiteral("请输入当前密码"));
    currentPasswordEdit_->addAction(
        IconProvider::instance().icon(ViewerIcon::Password, QSize(17, 17)),
        QLineEdit::LeadingPosition);
    currentPasswordEdit_->setText(currentPassword);
    newPasswordEdit_ = new QLineEdit(this);
    newPasswordEdit_->setPlaceholderText(QStringLiteral("请输入 12 至 256 位新密码"));
    newPasswordEdit_->addAction(
        IconProvider::instance().icon(ViewerIcon::Password, QSize(17, 17)),
        QLineEdit::LeadingPosition);
    confirmPasswordEdit_ = new QLineEdit(this);
    confirmPasswordEdit_->setPlaceholderText(QStringLiteral("请再次输入新密码"));
    confirmPasswordEdit_->addAction(
        IconProvider::instance().icon(ViewerIcon::Success, QSize(17, 17)),
        QLineEdit::LeadingPosition);
    for (auto *editor : {currentPasswordEdit_, newPasswordEdit_, confirmPasswordEdit_}) {
        editor->setEchoMode(QLineEdit::Password);
        editor->setMaxLength(256);
        editor->setInputMethodHints(Qt::ImhHiddenText | Qt::ImhSensitiveData | Qt::ImhNoPredictiveText);
    }

    errorLabel_ = new QLabel(this);
    errorLabel_->setObjectName(QStringLiteral("errorLabel"));
    errorLabel_->setWordWrap(true);
    errorLabel_->hide();

    auto *form = new QFormLayout;
    form->setFieldGrowthPolicy(QFormLayout::AllNonFixedFieldsGrow);
    form->addRow(QStringLiteral("当前密码"), currentPasswordEdit_);
    form->addRow(QStringLiteral("新密码"), newPasswordEdit_);
    form->addRow(QStringLiteral("确认新密码"), confirmPasswordEdit_);

    auto *buttons = new QDialogButtonBox(QDialogButtonBox::Cancel | QDialogButtonBox::Ok);
    submitButton_ = buttons->button(QDialogButtonBox::Ok);
    cancelButton_ = buttons->button(QDialogButtonBox::Cancel);
    submitButton_->setObjectName(QStringLiteral("changePasswordSubmitButton"));
    cancelButton_->setObjectName(QStringLiteral("changePasswordCancelButton"));
    submitButton_->setText(QStringLiteral("确认修改"));
    submitButton_->setProperty("primary", true);
    submitButton_->setMinimumHeight(36);
    submitButton_->setDefault(true);
    cancelButton_->setText(passwordChangeRequired ? QStringLiteral("返回登录") : QStringLiteral("取消"));

    contentLayout()->setSpacing(14);
    contentLayout()->addWidget(title);
    contentLayout()->addWidget(subtitle);
    contentLayout()->addSpacing(6);
    contentLayout()->addLayout(form);
    contentLayout()->addWidget(errorLabel_);
    contentLayout()->addWidget(buttons);

    connect(buttons, &QDialogButtonBox::accepted, this, &ChangePasswordDialog::submit);
    connect(buttons, &QDialogButtonBox::rejected, this, &ChangePasswordDialog::reject);
    connect(confirmPasswordEdit_, &QLineEdit::returnPressed, this, &ChangePasswordDialog::submit);
    connect(apiClient_, &ApiClient::passwordChangeSucceeded, this, [this]() {
        submitting_ = false;
        currentPasswordEdit_->clear();
        newPasswordEdit_->clear();
        confirmPasswordEdit_->clear();
        QDialog::accept();
    });
    connect(apiClient_, &ApiClient::passwordChangeFailed, this, &ChangePasswordDialog::passwordChangeFailed);

    currentPasswordEdit_->setFocus();
}

void ChangePasswordDialog::reject() {
    if (!submitting_) {
        QDialog::reject();
    }
}

void ChangePasswordDialog::submit() {
    if (submitting_) {
        return;
    }

    const QString currentPassword = currentPasswordEdit_->text();
    const QString newPassword = newPasswordEdit_->text();
    const QString confirmPassword = confirmPasswordEdit_->text();
    if (currentPassword.isEmpty() || newPassword.isEmpty() || confirmPassword.isEmpty()) {
        showError(QStringLiteral("请完整填写当前密码、新密码和确认密码。"));
        return;
    }
    if (newPassword.size() < 12 || newPassword.size() > 256) {
        showError(QStringLiteral("新密码长度必须为 12 至 256 位。"));
        newPasswordEdit_->selectAll();
        newPasswordEdit_->setFocus();
        return;
    }
    if (newPassword != confirmPassword) {
        showError(QStringLiteral("两次输入的新密码不一致。"));
        confirmPasswordEdit_->selectAll();
        confirmPasswordEdit_->setFocus();
        return;
    }
    if (newPassword == currentPassword) {
        showError(QStringLiteral("新密码不能与当前密码相同。"));
        newPasswordEdit_->selectAll();
        newPasswordEdit_->setFocus();
        return;
    }

    errorLabel_->hide();
    setSubmitting(true);
    apiClient_->changePassword(currentPassword, newPassword);
}

void ChangePasswordDialog::passwordChangeFailed(const QString &message) {
    setSubmitting(false);
    showError(message);
    currentPasswordEdit_->selectAll();
    currentPasswordEdit_->setFocus();
}

void ChangePasswordDialog::setSubmitting(bool submitting) {
    submitting_ = submitting;
    currentPasswordEdit_->setEnabled(!submitting);
    newPasswordEdit_->setEnabled(!submitting);
    confirmPasswordEdit_->setEnabled(!submitting);
    submitButton_->setEnabled(!submitting);
    submitButton_->setText(submitting ? QStringLiteral("正在修改…") : QStringLiteral("确认修改"));
    cancelButton_->setEnabled(!submitting);
}

void ChangePasswordDialog::showError(const QString &message) {
    errorLabel_->setText(message);
    errorLabel_->show();
}
