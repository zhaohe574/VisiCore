#include "HCNetSDK.h"

// 使用厂商 Linux 头文件独立验证 ABI，避免托管布局自证。
static_assert(sizeof(NET_DVR_STREAM_INFO) == 72);
static_assert(sizeof(NET_DVR_TIME) == 24);
static_assert(sizeof(NET_DVR_VOD_PARA) == 160);
static_assert(__builtin_offsetof(NET_DVR_VOD_PARA, dwFileIndex) == 132);
static_assert(__builtin_offsetof(NET_DVR_VOD_PARA, byUseAsyn) == 140);
static_assert(sizeof(NET_DVR_USER_LOGIN_INFO) == 416);
static_assert(__builtin_offsetof(NET_DVR_USER_LOGIN_INFO, cbLoginResult) == 264);
static_assert(__builtin_offsetof(NET_DVR_USER_LOGIN_INFO, bUseAsynLogin) == 280);
static_assert(sizeof(NET_DVR_DEVICEINFO_V40) == 344);
static_assert(sizeof(NET_DVR_PLAYCOND) == 116);
static_assert(sizeof(NET_DVR_SETUPALARM_PARAM) == 20);
static_assert(sizeof(NET_DVR_PREVIEWINFO) == 280);
static_assert(sizeof(NET_DVR_FILECOND_V50) == 424);
static_assert(__builtin_offsetof(NET_DVR_FILECOND_V50, struStartTime) == 72);
static_assert(__builtin_offsetof(NET_DVR_FILECOND_V50, dwTimeout) == 168);
static_assert(sizeof(NET_DVR_FINDDATA_V50) == 572);
static_assert(__builtin_offsetof(NET_DVR_FINDDATA_V50, dwFileSize) == 272);
static_assert(__builtin_offsetof(NET_DVR_FINDDATA_V50, dwTotalLenH) == 316);
static_assert(__builtin_offsetof(NET_DVR_FINDDATA_V50, dwTotalLenL) == 320);
static_assert(sizeof(NET_DVR_FILECOND_V40) == 160);
static_assert(__builtin_offsetof(NET_DVR_FILECOND_V40, byStreamType) == 128);
static_assert(sizeof(NET_DVR_FINDDATA_V40) == 320);
