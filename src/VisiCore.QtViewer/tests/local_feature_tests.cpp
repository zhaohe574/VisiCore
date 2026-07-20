#include "bookmark_store.h"
#include "connection_settings.h"
#include "export_controller.h"
#include "export_download_verifier.h"
#include "screenshot_service.h"

#include <QColor>
#include <QCoreApplication>
#include <QDir>
#include <QCryptographicHash>
#include <QFile>
#include <QImage>
#include <QStandardPaths>
#include <QTemporaryDir>
#include <QtTest>

class LocalFeatureTests final : public QObject {
    Q_OBJECT

private slots:
    void initTestCase() {
        QCoreApplication::setOrganizationName(QStringLiteral("VisiCoreLocalFeatureTests"));
        QCoreApplication::setApplicationName(QStringLiteral("VisiCoreLocalFeatureTests"));
        QStandardPaths::setTestModeEnabled(true);
    }

    void connectionSettingsRejectsUnsafeAddresses() {
        ConnectionSettings strictSettings(false);
        QVERIFY(strictSettings.isAllowed(QUrl(QStringLiteral("https://center.example/"))));
        QVERIFY(!strictSettings.isAllowed(QUrl(QStringLiteral("http://localhost:8080/"))));
        QVERIFY(!strictSettings.isAllowed(QUrl(QStringLiteral("https://user:password@center.example/"))));
        QVERIFY(!strictSettings.isAllowed(QUrl(QStringLiteral("https://center.example/?token=value"))));

        ConnectionSettings developmentSettings(true);
        QVERIFY(developmentSettings.isAllowed(QUrl(QStringLiteral("http://localhost:8080/"))));
        QVERIFY(developmentSettings.isAllowed(QUrl(QStringLiteral("http://127.0.0.1:8080/"))));
        QVERIFY(!developmentSettings.isAllowed(QUrl(QStringLiteral("http://center.example/"))));
    }

    void exportStateAndHashVerification() {
        PlaybackExportInfo queued;
        queued.status = QStringLiteral("Queued");
        QVERIFY(ExportController::isCancellable(queued));

        PlaybackExportInfo completed;
        completed.status = QStringLiteral("Completed");
        QVERIFY(!ExportController::isCancellable(completed));

        QCryptographicHash hash(QCryptographicHash::Sha256);
        hash.addData("verified export");
        const QByteArray digest = hash.result();
        QVERIFY(ExportDownloadVerifier::matchesSha256(
            QString::fromLatin1(digest.toHex()).toUpper(), digest));
        QVERIFY(!ExportDownloadVerifier::matchesSha256(QString(64, u'0'), digest));
    }

    void bookmarksAreIsolatedByAccount() {
        QTemporaryDir localDataDirectory;
        QVERIFY(localDataDirectory.isValid());
        BookmarkStore firstAccount(QStringLiteral("viewer-one"), localDataDirectory.path());
        BookmarkStore secondAccount(QStringLiteral("viewer-two"), localDataDirectory.path());

        const PlaybackBookmark bookmark{
            QUuid::createUuid(),
            QUuid::createUuid(),
            QStringLiteral("一号摄像头"),
            QDateTime::currentDateTimeUtc(),
            QDateTime::currentDateTimeUtc(),
            QStringLiteral("本地书签"),
            QStringLiteral("仅当前账号可见")};
        QVERIFY(firstAccount.save({bookmark}));
        QCOMPARE(firstAccount.load().size(), 1);
        QVERIFY(secondAccount.load().isEmpty());

    }

    void screenshotIsWrittenAsPng() {
        QTemporaryDir picturesDirectory;
        QVERIFY(picturesDirectory.isValid());
        const QUuid cameraId = QUuid::createUuid();
        QImage frame(3, 2, QImage::Format_ARGB32);
        frame.fill(Qt::red);

        const ScreenshotSaveResult result = ScreenshotService::savePng(
            QStringLiteral("viewer-screenshot"), cameraId, frame, picturesDirectory.path());
        QVERIFY2(result.succeeded(), qPrintable(result.errorMessage));
        const QImage restored(result.filePath);
        QVERIFY(!restored.isNull());
        QCOMPARE(restored.pixelColor(0, 0), QColor(Qt::red));
        QVERIFY(QFile::remove(result.filePath));
    }
};

QTEST_GUILESS_MAIN(LocalFeatureTests)

#include "local_feature_tests.moc"
