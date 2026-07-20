#pragma once

#include <QByteArray>
#include <QCryptographicHash>
#include <QString>

namespace ExportDownloadVerifier {
inline bool matchesSha256(const QString &expectedSha256, const QByteArray &digest) {
    return expectedSha256.trimmed().toUpper() ==
           QString::fromLatin1(digest.toHex()).toUpper();
}

inline bool matchesSha256(const QString &expectedSha256, const QCryptographicHash &hash) {
    return matchesSha256(expectedSha256, hash.result());
}
}
