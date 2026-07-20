#pragma once

#include <QDialog>
#include <QDialogButtonBox>

class QLabel;
class QVBoxLayout;

enum class AppMessageTone {
    Information,
    Success,
    Warning,
    Critical,
    Question,
};

class AppDialog : public QDialog {
    Q_OBJECT

public:
    explicit AppDialog(const QString &title, QWidget *parent = nullptr);

    [[nodiscard]] QVBoxLayout *contentLayout() const;
    void setDialogTitle(const QString &title);
    void setContentMargins(int left, int top, int right, int bottom);

    static QDialogButtonBox::StandardButton message(
        QWidget *parent,
        const QString &title,
        const QString &text,
        AppMessageTone tone,
        QDialogButtonBox::StandardButtons buttons = QDialogButtonBox::Ok,
        QDialogButtonBox::StandardButton defaultButton = QDialogButtonBox::Ok);
    static void information(QWidget *parent, const QString &title, const QString &text);
    static void success(QWidget *parent, const QString &title, const QString &text);
    static void warning(QWidget *parent, const QString &title, const QString &text);
    static void critical(QWidget *parent, const QString &title, const QString &text);
    static QDialogButtonBox::StandardButton question(
        QWidget *parent,
        const QString &title,
        const QString &text,
        QDialogButtonBox::StandardButton defaultButton = QDialogButtonBox::No);

private:
    QLabel *titleLabel_;
    QVBoxLayout *contentLayout_;
};
