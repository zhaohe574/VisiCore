#include "app_dialog.h"

#include "icon_provider.h"
#include "theme_manager.h"

#include <QAbstractButton>
#include <QHBoxLayout>
#include <QLabel>
#include <QMouseEvent>
#include <QPushButton>
#include <QToolButton>
#include <QVBoxLayout>
#include <QWindow>

namespace {
class DialogTitleBar final : public QWidget {
public:
    DialogTitleBar(QLabel *titleLabel, QWidget *parent)
        : QWidget(parent), titleLabel_(titleLabel) {
        setObjectName(QStringLiteral("dialogTitleBar"));
        setAttribute(Qt::WA_StyledBackground, true);
        setFixedHeight(38);

        auto *appIcon = new QLabel(this);
        appIcon->setPixmap(IconProvider::instance()
                               .icon(ViewerIcon::Camera, ThemeManager::instance().color(ThemeColor::PrimaryHover), QSize(17, 17))
                               .pixmap(QSize(17, 17)));
        titleLabel_->setObjectName(QStringLiteral("dialogTitle"));
        titleLabel_->setAttribute(Qt::WA_TransparentForMouseEvents, true);

        auto *closeButton = new QToolButton(this);
        closeButton->setObjectName(QStringLiteral("dialogCloseButton"));
        closeButton->setIcon(IconProvider::instance().icon(ViewerIcon::Close, QSize(16, 16)));
        closeButton->setIconSize(QSize(16, 16));
        closeButton->setToolTip(QStringLiteral("关闭"));
        closeButton->setAccessibleName(QStringLiteral("关闭"));
        closeButton->setFocusPolicy(Qt::NoFocus);

        auto *layout = new QHBoxLayout(this);
        layout->setContentsMargins(11, 0, 0, 0);
        layout->setSpacing(8);
        layout->addWidget(appIcon);
        layout->addWidget(titleLabel_);
        layout->addStretch(1);
        layout->addWidget(closeButton);

        connect(closeButton, &QToolButton::clicked, this, [this]() {
            if (QWidget *topLevel = window()) {
                topLevel->close();
            }
        });
    }

protected:
    void mousePressEvent(QMouseEvent *event) override {
        if (event->button() != Qt::LeftButton) {
            QWidget::mousePressEvent(event);
            return;
        }
        dragOffset_ = event->globalPosition().toPoint() - window()->frameGeometry().topLeft();
        if (QWindow *handle = window()->windowHandle(); handle != nullptr && handle->startSystemMove()) {
            event->accept();
            return;
        }
        dragging_ = true;
        event->accept();
    }

    void mouseMoveEvent(QMouseEvent *event) override {
        if (dragging_ && (event->buttons() & Qt::LeftButton)) {
            window()->move(event->globalPosition().toPoint() - dragOffset_);
            event->accept();
            return;
        }
        QWidget::mouseMoveEvent(event);
    }

    void mouseReleaseEvent(QMouseEvent *event) override {
        dragging_ = false;
        QWidget::mouseReleaseEvent(event);
    }

private:
    QLabel *titleLabel_;
    QPoint dragOffset_;
    bool dragging_ = false;
};

ViewerIcon messageIcon(AppMessageTone tone) {
    switch (tone) {
        case AppMessageTone::Success: return ViewerIcon::Success;
        case AppMessageTone::Warning:
        case AppMessageTone::Critical: return ViewerIcon::Alert;
        case AppMessageTone::Question: return ViewerIcon::Help;
        case AppMessageTone::Information: return ViewerIcon::Video;
    }
    return ViewerIcon::Video;
}

QColor messageColor(AppMessageTone tone) {
    switch (tone) {
        case AppMessageTone::Success: return ThemeManager::instance().color(ThemeColor::Success);
        case AppMessageTone::Warning: return ThemeManager::instance().color(ThemeColor::Warning);
        case AppMessageTone::Critical: return ThemeManager::instance().color(ThemeColor::Danger);
        case AppMessageTone::Question: return ThemeManager::instance().color(ThemeColor::Focus);
        case AppMessageTone::Information: return ThemeManager::instance().color(ThemeColor::Information);
    }
    return ThemeManager::instance().color(ThemeColor::Information);
}

void localizeButtons(QDialogButtonBox *buttonBox) {
    const auto setText = [buttonBox](QDialogButtonBox::StandardButton button, const QString &text) {
        if (QPushButton *target = buttonBox->button(button)) {
            target->setText(text);
        }
    };
    setText(QDialogButtonBox::Ok, QStringLiteral("确定"));
    setText(QDialogButtonBox::Yes, QStringLiteral("确认"));
    setText(QDialogButtonBox::No, QStringLiteral("取消"));
    setText(QDialogButtonBox::Cancel, QStringLiteral("取消"));
    setText(QDialogButtonBox::Close, QStringLiteral("关闭"));
    setText(QDialogButtonBox::Retry, QStringLiteral("重试"));
}
}

AppDialog::AppDialog(const QString &title, QWidget *parent)
    : QDialog(parent),
      titleLabel_(new QLabel(title, this)),
      contentLayout_(new QVBoxLayout) {
    setObjectName(QStringLiteral("appDialog"));
    setWindowTitle(title);
    setWindowFlags(Qt::Dialog | Qt::FramelessWindowHint | Qt::WindowSystemMenuHint);
    setAttribute(Qt::WA_StyledBackground, true);
    setSizeGripEnabled(false);

    auto *content = new QWidget(this);
    content->setObjectName(QStringLiteral("dialogContent"));
    content->setAttribute(Qt::WA_StyledBackground, true);
    content->setLayout(contentLayout_);
    contentLayout_->setContentsMargins(28, 24, 28, 26);
    contentLayout_->setSpacing(14);

    auto *layout = new QVBoxLayout(this);
    layout->setContentsMargins(1, 1, 1, 1);
    layout->setSpacing(0);
    layout->addWidget(new DialogTitleBar(titleLabel_, this));
    layout->addWidget(content, 1);
}

QVBoxLayout *AppDialog::contentLayout() const {
    return contentLayout_;
}

void AppDialog::setDialogTitle(const QString &title) {
    setWindowTitle(title);
    titleLabel_->setText(title);
}

void AppDialog::setContentMargins(int left, int top, int right, int bottom) {
    contentLayout_->setContentsMargins(left, top, right, bottom);
}

QDialogButtonBox::StandardButton AppDialog::message(
    QWidget *parent,
    const QString &title,
    const QString &text,
    AppMessageTone tone,
    QDialogButtonBox::StandardButtons buttons,
    QDialogButtonBox::StandardButton defaultButton) {
    AppDialog dialog(title, parent);
    dialog.setModal(true);
    dialog.setMinimumWidth(430);
    dialog.setMaximumWidth(620);

    auto *messageRow = new QHBoxLayout;
    messageRow->setContentsMargins(0, 2, 0, 4);
    messageRow->setSpacing(14);
    auto *iconLabel = new QLabel(&dialog);
    iconLabel->setObjectName(QStringLiteral("dialogMessageIcon"));
    iconLabel->setFixedSize(32, 32);
    iconLabel->setPixmap(IconProvider::instance()
                             .icon(messageIcon(tone), messageColor(tone), QSize(28, 28))
                             .pixmap(QSize(28, 28)));
    iconLabel->setAlignment(Qt::AlignCenter);
    auto *messageLabel = new QLabel(text, &dialog);
    messageLabel->setObjectName(QStringLiteral("dialogMessage"));
    messageLabel->setWordWrap(true);
    messageLabel->setTextInteractionFlags(Qt::TextSelectableByMouse);
    messageLabel->setMinimumWidth(310);
    messageRow->addWidget(iconLabel, 0, Qt::AlignTop);
    messageRow->addWidget(messageLabel, 1);

    auto *buttonBox = new QDialogButtonBox(buttons, &dialog);
    localizeButtons(buttonBox);
    if (QPushButton *defaultPushButton = buttonBox->button(defaultButton)) {
        defaultPushButton->setDefault(true);
        defaultPushButton->setFocus();
    }

    QDialogButtonBox::StandardButton selectedButton = QDialogButtonBox::NoButton;
    QObject::connect(buttonBox, &QDialogButtonBox::clicked, &dialog, [&](QAbstractButton *clickedButton) {
        selectedButton = buttonBox->standardButton(clickedButton);
        dialog.done(QDialog::Accepted);
    });
    dialog.contentLayout()->addLayout(messageRow);
    dialog.contentLayout()->addWidget(buttonBox);
    dialog.exec();
    return selectedButton;
}

void AppDialog::information(QWidget *parent, const QString &title, const QString &text) {
    message(parent, title, text, AppMessageTone::Information);
}

void AppDialog::success(QWidget *parent, const QString &title, const QString &text) {
    message(parent, title, text, AppMessageTone::Success);
}

void AppDialog::warning(QWidget *parent, const QString &title, const QString &text) {
    message(parent, title, text, AppMessageTone::Warning);
}

void AppDialog::critical(QWidget *parent, const QString &title, const QString &text) {
    message(parent, title, text, AppMessageTone::Critical);
}

QDialogButtonBox::StandardButton AppDialog::question(
    QWidget *parent,
    const QString &title,
    const QString &text,
    QDialogButtonBox::StandardButton defaultButton) {
    return message(
        parent,
        title,
        text,
        AppMessageTone::Question,
        QDialogButtonBox::Yes | QDialogButtonBox::No,
        defaultButton);
}
