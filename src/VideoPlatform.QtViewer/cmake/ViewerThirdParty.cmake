include_guard(GLOBAL)

include(FetchContent)

set(VIDEOPLATFORM_QLEMENTINE_VERSION "1.4.2")
set(VIDEOPLATFORM_QLEMENTINE_ARCHIVE_SHA256 "ED5196E6E04614DB65F9A5F813EB35BF05076B7BCE07892DB33E85CF7B598616")
set(VIDEOPLATFORM_QT_ADS_VERSION "5.0.0")
set(VIDEOPLATFORM_QT_ADS_ARCHIVE_SHA256 "12AFA0F1EB32FB123568DC2435E8C12D0E87A49934D8BA4EF746F9DE2BFC799C")
set(VIDEOPLATFORM_LUCIDE_VERSION "1.23.0")
set(VIDEOPLATFORM_LUCIDE_ARCHIVE_SHA256 "5160BC6E37FDA62286B36610BFA314DC7F5D53218DFB87F1F5540F40414D8B9B")

set(VIDEOPLATFORM_QLEMENTINE_SOURCE_ARCHIVE "" CACHE FILEPATH
    "Qlementine 1.4.2 已审批源码归档；为空时从固定官方地址下载。")
set(VIDEOPLATFORM_QT_ADS_SOURCE_ARCHIVE "" CACHE FILEPATH
    "Qt ADS 5.0.0 已审批源码归档；为空时从固定官方地址下载。")
set(VIDEOPLATFORM_LUCIDE_SOURCE_ARCHIVE "" CACHE FILEPATH
    "Lucide 1.23.0 已审批源码归档；为空时从固定官方地址下载。")

function(videoplatform_resolve_archive output_variable local_archive official_url description)
    if(local_archive)
        get_filename_component(resolved_archive "${local_archive}" ABSOLUTE)
        if(NOT EXISTS "${resolved_archive}" OR IS_DIRECTORY "${resolved_archive}")
            message(FATAL_ERROR "未找到${description}源码归档：${resolved_archive}")
        endif()
        set(${output_variable} "${resolved_archive}" PARENT_SCOPE)
        return()
    endif()

    set(${output_variable} "${official_url}" PARENT_SCOPE)
endfunction()

macro(videoplatform_populate dependency_name)
    FetchContent_GetProperties(${dependency_name})
    if(NOT ${dependency_name}_POPULATED)
        if(POLICY CMP0169)
            cmake_policy(PUSH)
            cmake_policy(SET CMP0169 OLD)
        endif()
        FetchContent_Populate(${dependency_name})
        if(POLICY CMP0169)
            cmake_policy(POP)
        endif()
    endif()
endmacro()

videoplatform_resolve_archive(
    qlementine_archive
    "${VIDEOPLATFORM_QLEMENTINE_SOURCE_ARCHIVE}"
    "https://github.com/oclero/qlementine/archive/refs/tags/v1.4.2.tar.gz"
    "Qlementine 1.4.2")
FetchContent_Declare(qlementine
    URL "${qlementine_archive}"
    URL_HASH "SHA256=${VIDEOPLATFORM_QLEMENTINE_ARCHIVE_SHA256}"
    DOWNLOAD_EXTRACT_TIMESTAMP TRUE)
videoplatform_populate(qlementine)
set(QLEMENTINE_SANDBOX OFF CACHE BOOL "" FORCE)
set(QLEMENTINE_SHOWCASE OFF CACHE BOOL "" FORCE)
add_subdirectory("${qlementine_SOURCE_DIR}" "${qlementine_BINARY_DIR}" EXCLUDE_FROM_ALL)

videoplatform_resolve_archive(
    qt_ads_archive
    "${VIDEOPLATFORM_QT_ADS_SOURCE_ARCHIVE}"
    "https://github.com/githubuser0xFFFF/Qt-Advanced-Docking-System/archive/refs/tags/5.0.0.tar.gz"
    "Qt ADS 5.0.0")
FetchContent_Declare(qt_ads
    URL "${qt_ads_archive}"
    URL_HASH "SHA256=${VIDEOPLATFORM_QT_ADS_ARCHIVE_SHA256}"
    DOWNLOAD_EXTRACT_TIMESTAMP TRUE)
videoplatform_populate(qt_ads)
set(ADS_VERSION "${VIDEOPLATFORM_QT_ADS_VERSION}")
set(ADS_PLATFORM_DIR "x64")
set(BUILD_STATIC OFF CACHE BOOL "" FORCE)
set(BUILD_EXAMPLES OFF CACHE BOOL "" FORCE)
set(QT_VERSION_MAJOR 6)
add_subdirectory("${qt_ads_SOURCE_DIR}" "${qt_ads_BINARY_DIR}" EXCLUDE_FROM_ALL)

videoplatform_resolve_archive(
    lucide_archive
    "${VIDEOPLATFORM_LUCIDE_SOURCE_ARCHIVE}"
    "https://github.com/lucide-icons/lucide/archive/refs/tags/1.23.0.tar.gz"
    "Lucide 1.23.0")
FetchContent_Declare(lucide
    URL "${lucide_archive}"
    URL_HASH "SHA256=${VIDEOPLATFORM_LUCIDE_ARCHIVE_SHA256}"
    DOWNLOAD_EXTRACT_TIMESTAMP TRUE)
videoplatform_populate(lucide)

set(VIDEOPLATFORM_LUCIDE_ICON_NAMES
    camera
    folder
    video
    play
    pause
    square
    refresh-cw
    search
    star
    save
    layout-grid
    maximize-2
    minimize-2
    panel-left
    panel-right
    panel-bottom
    lock
    rotate-ccw
    user
    log-out
    key-round
    chevron-down
    skip-back
    skip-forward
    zoom-in
    zoom-out
    calendar-search
    circle-alert
    circle-check
    move-up-left
    move-up-right
    move-down-left
    move-down-right
    arrow-up
    arrow-down
    arrow-left
    arrow-right
    plus
    minus
    x
    settings)

set(VIDEOPLATFORM_LUCIDE_ICON_FILES "")
foreach(icon_name IN LISTS VIDEOPLATFORM_LUCIDE_ICON_NAMES)
    list(APPEND VIDEOPLATFORM_LUCIDE_ICON_FILES "${lucide_SOURCE_DIR}/icons/${icon_name}.svg")
endforeach()

# Lucide 1.23.0 已调整这三个图标的正式名称，资源别名保持查看端接口稳定。
set(lucide_unlock_icon "${lucide_SOURCE_DIR}/icons/lock-open.svg")
set(lucide_more_horizontal_icon "${lucide_SOURCE_DIR}/icons/ellipsis.svg")
set(lucide_circle_help_icon "${lucide_SOURCE_DIR}/icons/circle-question-mark.svg")
set_source_files_properties("${lucide_unlock_icon}" PROPERTIES QT_RESOURCE_ALIAS "unlock.svg")
set_source_files_properties("${lucide_more_horizontal_icon}" PROPERTIES QT_RESOURCE_ALIAS "more-horizontal.svg")
set_source_files_properties("${lucide_circle_help_icon}" PROPERTIES QT_RESOURCE_ALIAS "circle-help.svg")
list(APPEND VIDEOPLATFORM_LUCIDE_ICON_FILES
    "${lucide_unlock_icon}"
    "${lucide_more_horizontal_icon}"
    "${lucide_circle_help_icon}")
