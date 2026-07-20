cmake_minimum_required(VERSION 3.24)
file(READ "${CMAKE_CURRENT_LIST_DIR}/../../VERSION" VISICORE_VIEWER_VERSION_RAW)
string(STRIP "${VISICORE_VIEWER_VERSION_RAW}" VISICORE_VIEWER_VERSION)
if(NOT VISICORE_VIEWER_VERSION MATCHES "^[0-9]+\\.[0-9]+\\.[0-9]+$")
    message(FATAL_ERROR "根目录 VERSION 必须是 x.y.z 格式。")
endif()
project(VisiCoreQtViewer VERSION ${VISICORE_VIEWER_VERSION} LANGUAGES CXX)

set(CMAKE_INSTALL_BINDIR ".")

set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_AUTOMOC ON)
set(CMAKE_AUTOUIC ON)
set(CMAKE_AUTORCC ON)

option(VISICORE_RELEASE_BUILD "启用受控 Windows 发行构建校验" OFF)
set(VISICORE_MPV_ROOT "$ENV{MPV_ROOT}" CACHE PATH "已审批 libmpv x64 运行时目录")
set(VISICORE_MPV_RUNTIME_VERSION "" CACHE STRING "已审批 libmpv 运行时版本")
set(VISICORE_MPV_RUNTIME_SOURCE "" CACHE STRING "已审批 libmpv 运行时来源")
set(VISICORE_MPV_RUNTIME_SHA256 "" CACHE STRING "已审批 libmpv-2.dll 的 SHA-256")
set(VISICORE_MPV_RUNTIME_LICENSE_FILE "" CACHE FILEPATH "已审批 libmpv 运行时许可证文件")

find_package(Qt6 6.5 REQUIRED COMPONENTS Core Network Widgets OpenGLWidgets Svg)

include(CTest)

if(NOT CMAKE_SIZEOF_VOID_P EQUAL 8)
    message(FATAL_ERROR "Windows 查看客户端只支持 64 位工具链。")
endif()

include(cmake/ViewerThirdParty.cmake)

qt_add_executable(VisiCoreQtViewer WIN32
    src/main.cpp
    src/crash_reporter.h
    src/crash_reporter.cpp
    src/models.h
    src/viewer_logic.h
    src/viewer_logic.cpp
    src/apiclient.h
    src/apiclient.cpp
    src/connection_settings.h
    src/connection_settings.cpp
    src/connection_settings_dialog.h
    src/connection_settings_dialog.cpp
    src/login_dialog.h
    src/login_dialog.cpp
    src/change_password_dialog.h
    src/change_password_dialog.cpp
    src/app_dialog.h
    src/app_dialog.cpp
    src/theme_manager.h
    src/theme_manager.cpp
    src/icon_provider.h
    src/icon_provider.cpp
    src/viewer_action_registry.h
    src/viewer_action_registry.cpp
    src/window_title_bar.h
    src/window_title_bar.cpp
    src/viewer_ui_types.h
    src/viewer_ui_settings.h
    src/viewer_ui_settings.cpp
    src/viewer_startup_state.h
    src/viewer_startup_state.cpp
    src/workspace_controller.h
    src/workspace_controller.cpp
    src/preview_controller.h
    src/preview_controller.cpp
    src/playback_controller.h
    src/playback_controller.cpp
    src/ptz_controller.h
    src/ptz_controller.cpp
    src/dock_layout_controller.h
    src/dock_layout_controller.cpp
    src/export_controller.h
    src/export_controller.cpp
    src/export_tasks_panel.h
    src/export_tasks_panel.cpp
    src/bookmark_store.h
    src/bookmark_store.cpp
    src/screenshot_service.h
    src/screenshot_service.cpp
    src/camera_tree_widget.h
    src/camera_tree_widget.cpp
    src/mpv_player_widget.h
    src/mpv_player_widget.cpp
    src/recording_timeline_widget.h
    src/recording_timeline_widget.cpp
    src/video_tile_widget.h
    src/video_tile_widget.cpp
    src/main_window.h
    src/main_window.cpp)

qt_add_resources(VisiCoreQtViewer visicore_lucide_icons
    PREFIX "/icons/lucide"
    BASE "${lucide_SOURCE_DIR}/icons"
    FILES ${VISICORE_LUCIDE_ICON_FILES})

set_source_files_properties(src/visicore_dark_theme.json
    PROPERTIES QT_RESOURCE_ALIAS "visicore-dark.json")
qt_add_resources(VisiCoreQtViewer visicore_theme
    PREFIX "/themes"
    FILES src/visicore_dark_theme.json)

target_link_libraries(VisiCoreQtViewer PRIVATE
    Qt6::Core
    Qt6::Network
    Qt6::Widgets
    Qt6::OpenGLWidgets
    Qt6::Svg
    qlementine
    ads::qtadvanceddocking-qt6)

target_compile_definitions(VisiCoreQtViewer PRIVATE
    QT_NO_CAST_FROM_ASCII
    QT_NO_CAST_TO_ASCII
    VISICORE_QLEMENTINE_VERSION="${VISICORE_QLEMENTINE_VERSION}"
    VISICORE_QT_ADS_VERSION="${VISICORE_QT_ADS_VERSION}"
    VISICORE_LUCIDE_VERSION="${VISICORE_LUCIDE_VERSION}"
    NOMINMAX
    WIN32_LEAN_AND_MEAN)

if(MSVC)
    target_compile_options(VisiCoreQtViewer PRIVATE /W4 /permissive- /utf-8)
else()
    target_compile_options(VisiCoreQtViewer PRIVATE -Wall -Wextra -Wpedantic)
endif()

if(BUILD_TESTING)
    find_package(Qt6 6.5 REQUIRED COMPONENTS Test)

    qt_add_executable(VisiCoreQtViewerLogicTests
        tests/viewer_logic_tests.cpp
        src/models.h
        src/viewer_logic.h
        src/viewer_logic.cpp
        src/viewer_ui_types.h
        src/viewer_ui_settings.h
        src/viewer_ui_settings.cpp
        src/viewer_startup_state.h
        src/viewer_startup_state.cpp
        src/recording_timeline_widget.h
        src/recording_timeline_widget.cpp)

    target_include_directories(VisiCoreQtViewerLogicTests PRIVATE src)

    target_link_libraries(VisiCoreQtViewerLogicTests PRIVATE
        Qt6::Core
        Qt6::Widgets
        Qt6::Test)

    target_compile_definitions(VisiCoreQtViewerLogicTests PRIVATE
        QT_NO_CAST_FROM_ASCII
        QT_NO_CAST_TO_ASCII
        NOMINMAX
        WIN32_LEAN_AND_MEAN)

    if(MSVC)
        target_compile_options(VisiCoreQtViewerLogicTests PRIVATE /W4 /permissive- /utf-8)
    else()
        target_compile_options(VisiCoreQtViewerLogicTests PRIVATE -Wall -Wextra -Wpedantic)
    endif()

    add_test(NAME VisiCoreQtViewer.Logic COMMAND VisiCoreQtViewerLogicTests)
    set_property(TEST VisiCoreQtViewer.Logic PROPERTY ENVIRONMENT "QT_QPA_PLATFORM=offscreen")

    qt_add_executable(VisiCoreQtViewerDockTests
        tests/dock_layout_controller_tests.cpp
        src/viewer_ui_types.h
        src/viewer_ui_settings.h
        src/viewer_ui_settings.cpp
        src/viewer_startup_state.h
        src/viewer_startup_state.cpp
        src/dock_layout_controller.h
        src/dock_layout_controller.cpp)

    target_include_directories(VisiCoreQtViewerDockTests PRIVATE src)

    target_link_libraries(VisiCoreQtViewerDockTests PRIVATE
        Qt6::Core
        Qt6::Widgets
        Qt6::Test
        ads::qtadvanceddocking-qt6)

    target_compile_definitions(VisiCoreQtViewerDockTests PRIVATE
        QT_NO_CAST_FROM_ASCII
        QT_NO_CAST_TO_ASCII
        NOMINMAX
        WIN32_LEAN_AND_MEAN)

    if(MSVC)
        target_compile_options(VisiCoreQtViewerDockTests PRIVATE /W4 /permissive- /utf-8)
    else()
        target_compile_options(VisiCoreQtViewerDockTests PRIVATE -Wall -Wextra -Wpedantic)
    endif()

    add_test(NAME VisiCoreQtViewer.DockLayout COMMAND VisiCoreQtViewerDockTests)
    set_property(TEST VisiCoreQtViewer.DockLayout PROPERTY ENVIRONMENT "QT_QPA_PLATFORM=offscreen")

    qt_add_executable(VisiCoreQtViewerTitleBarTests
        tests/window_title_bar_tests.cpp
        src/icon_provider.h
        src/icon_provider.cpp
        src/theme_manager.h
        src/theme_manager.cpp
        src/window_title_bar.h
        src/window_title_bar.cpp
        src/viewer_ui_types.h)

    target_include_directories(VisiCoreQtViewerTitleBarTests PRIVATE src)

    target_link_libraries(VisiCoreQtViewerTitleBarTests PRIVATE
        Qt6::Core
        Qt6::Widgets
        Qt6::Svg
        Qt6::Test
        qlementine)

    target_compile_definitions(VisiCoreQtViewerTitleBarTests PRIVATE
        QT_NO_CAST_FROM_ASCII
        QT_NO_CAST_TO_ASCII
        NOMINMAX
        WIN32_LEAN_AND_MEAN)

    if(MSVC)
        target_compile_options(VisiCoreQtViewerTitleBarTests PRIVATE /W4 /permissive- /utf-8)
    else()
        target_compile_options(VisiCoreQtViewerTitleBarTests PRIVATE -Wall -Wextra -Wpedantic)
    endif()

    add_test(NAME VisiCoreQtViewer.TitleBar COMMAND VisiCoreQtViewerTitleBarTests)
    set_property(TEST VisiCoreQtViewer.TitleBar PROPERTY ENVIRONMENT "QT_QPA_PLATFORM=offscreen")

    qt_add_executable(VisiCoreQtViewerControllerTests
        tests/workspace_controllers_tests.cpp
        src/models.h
        src/viewer_ui_types.h
        src/workspace_controller.h
        src/workspace_controller.cpp
        src/preview_controller.h
        src/preview_controller.cpp
        src/playback_controller.h
        src/playback_controller.cpp
        src/ptz_controller.h
        src/ptz_controller.cpp)

    target_include_directories(VisiCoreQtViewerControllerTests PRIVATE src)

    target_link_libraries(VisiCoreQtViewerControllerTests PRIVATE
        Qt6::Core
        Qt6::Test)

    target_compile_definitions(VisiCoreQtViewerControllerTests PRIVATE
        QT_NO_CAST_FROM_ASCII
        QT_NO_CAST_TO_ASCII
        NOMINMAX
        WIN32_LEAN_AND_MEAN)

    if(MSVC)
        target_compile_options(VisiCoreQtViewerControllerTests PRIVATE /W4 /permissive- /utf-8)
    else()
        target_compile_options(VisiCoreQtViewerControllerTests PRIVATE -Wall -Wextra -Wpedantic)
    endif()

    add_test(NAME VisiCoreQtViewer.Controllers COMMAND VisiCoreQtViewerControllerTests)
    set_property(TEST VisiCoreQtViewer.Controllers PROPERTY ENVIRONMENT "QT_QPA_PLATFORM=offscreen")

    qt_add_executable(VisiCoreQtViewerInteractionTests
        tests/main_window_interaction_tests.cpp
        src/crash_reporter.h
        src/crash_reporter.cpp
        src/models.h
        src/viewer_logic.h
        src/viewer_logic.cpp
        src/apiclient.h
        src/apiclient.cpp
        src/connection_settings.h
        src/connection_settings.cpp
        src/connection_settings_dialog.h
        src/connection_settings_dialog.cpp
        src/login_dialog.h
        src/login_dialog.cpp
        src/change_password_dialog.h
        src/change_password_dialog.cpp
        src/app_dialog.h
        src/app_dialog.cpp
        src/theme_manager.h
        src/theme_manager.cpp
        src/icon_provider.h
        src/icon_provider.cpp
        src/viewer_action_registry.h
        src/viewer_action_registry.cpp
        src/window_title_bar.h
        src/window_title_bar.cpp
        src/viewer_ui_types.h
        src/viewer_ui_settings.h
        src/viewer_ui_settings.cpp
        src/viewer_startup_state.h
        src/viewer_startup_state.cpp
        src/workspace_controller.h
        src/workspace_controller.cpp
        src/preview_controller.h
        src/preview_controller.cpp
        src/playback_controller.h
        src/playback_controller.cpp
        src/ptz_controller.h
        src/ptz_controller.cpp
        src/dock_layout_controller.h
        src/dock_layout_controller.cpp
        src/export_controller.h
        src/export_controller.cpp
        src/export_tasks_panel.h
        src/export_tasks_panel.cpp
        src/bookmark_store.h
        src/bookmark_store.cpp
        src/screenshot_service.h
        src/screenshot_service.cpp
        src/camera_tree_widget.h
        src/camera_tree_widget.cpp
        src/mpv_player_widget.h
        src/mpv_player_widget.cpp
        src/recording_timeline_widget.h
        src/recording_timeline_widget.cpp
        src/video_tile_widget.h
        src/video_tile_widget.cpp
        src/main_window.h
        src/main_window.cpp)

    qt_add_resources(VisiCoreQtViewerInteractionTests visicore_interaction_lucide_icons
        PREFIX "/icons/lucide"
        BASE "${lucide_SOURCE_DIR}/icons"
        FILES ${VISICORE_LUCIDE_ICON_FILES})
    qt_add_resources(VisiCoreQtViewerInteractionTests visicore_interaction_theme
        PREFIX "/themes"
        FILES src/visicore_dark_theme.json)

    target_include_directories(VisiCoreQtViewerInteractionTests PRIVATE src)

    target_link_libraries(VisiCoreQtViewerInteractionTests PRIVATE
        Qt6::Core
        Qt6::Network
        Qt6::Widgets
        Qt6::OpenGLWidgets
        Qt6::Svg
        Qt6::Test
        qlementine
        ads::qtadvanceddocking-qt6)

    target_compile_definitions(VisiCoreQtViewerInteractionTests PRIVATE
        QT_NO_CAST_FROM_ASCII
        QT_NO_CAST_TO_ASCII
        NOMINMAX
        WIN32_LEAN_AND_MEAN)

    if(MSVC)
        target_compile_options(VisiCoreQtViewerInteractionTests PRIVATE /W4 /permissive- /utf-8)
    else()
        target_compile_options(VisiCoreQtViewerInteractionTests PRIVATE -Wall -Wextra -Wpedantic)
    endif()

    add_test(NAME VisiCoreQtViewer.MainWindowInteraction COMMAND VisiCoreQtViewerInteractionTests)
    set_property(TEST VisiCoreQtViewer.MainWindowInteraction PROPERTY ENVIRONMENT "QT_QPA_PLATFORM=offscreen")

    qt_add_executable(VisiCoreQtViewerLocalFeatureTests
        tests/local_feature_tests.cpp
        src/models.h
        src/connection_settings.h
        src/connection_settings.cpp
        src/export_controller.h
        src/export_controller.cpp
        src/export_download_verifier.h
        src/bookmark_store.h
        src/bookmark_store.cpp
        src/screenshot_service.h
        src/screenshot_service.cpp)

    target_include_directories(VisiCoreQtViewerLocalFeatureTests PRIVATE src)

    target_link_libraries(VisiCoreQtViewerLocalFeatureTests PRIVATE
        Qt6::Core
        Qt6::Gui
        Qt6::Test)

    target_compile_definitions(VisiCoreQtViewerLocalFeatureTests PRIVATE
        QT_NO_CAST_FROM_ASCII
        QT_NO_CAST_TO_ASCII
        NOMINMAX
        WIN32_LEAN_AND_MEAN)

    if(MSVC)
        target_compile_options(VisiCoreQtViewerLocalFeatureTests PRIVATE /W4 /permissive- /utf-8)
    else()
        target_compile_options(VisiCoreQtViewerLocalFeatureTests PRIVATE -Wall -Wextra -Wpedantic)
    endif()

    add_test(NAME VisiCoreQtViewer.LocalFeatures COMMAND VisiCoreQtViewerLocalFeatureTests)
    set_property(TEST VisiCoreQtViewer.LocalFeatures PROPERTY ENVIRONMENT "QT_QPA_PLATFORM=offscreen")

    if(WIN32)
        get_filename_component(VIEWER_TEST_COMPILER_RUNTIME_DIRECTORY "${CMAKE_CXX_COMPILER}" DIRECTORY)
        set_property(TEST VisiCoreQtViewer.Logic PROPERTY ENVIRONMENT_MODIFICATION
            "PATH=path_list_prepend:$<TARGET_FILE_DIR:Qt6::Core>"
            "PATH=path_list_prepend:${VIEWER_TEST_COMPILER_RUNTIME_DIRECTORY}")
        set_property(TEST VisiCoreQtViewer.DockLayout PROPERTY ENVIRONMENT_MODIFICATION
            "PATH=path_list_prepend:$<TARGET_FILE_DIR:Qt6::Core>"
            "PATH=path_list_prepend:$<TARGET_FILE_DIR:qtadvanceddocking-qt6>"
            "PATH=path_list_prepend:${VIEWER_TEST_COMPILER_RUNTIME_DIRECTORY}")
        set_property(TEST VisiCoreQtViewer.TitleBar PROPERTY ENVIRONMENT_MODIFICATION
            "PATH=path_list_prepend:$<TARGET_FILE_DIR:Qt6::Core>"
            "PATH=path_list_prepend:${VIEWER_TEST_COMPILER_RUNTIME_DIRECTORY}")
        set_property(TEST VisiCoreQtViewer.Controllers PROPERTY ENVIRONMENT_MODIFICATION
            "PATH=path_list_prepend:$<TARGET_FILE_DIR:Qt6::Core>"
            "PATH=path_list_prepend:${VIEWER_TEST_COMPILER_RUNTIME_DIRECTORY}")
        set_property(TEST VisiCoreQtViewer.MainWindowInteraction PROPERTY ENVIRONMENT_MODIFICATION
            "PATH=path_list_prepend:$<TARGET_FILE_DIR:Qt6::Core>"
            "PATH=path_list_prepend:$<TARGET_FILE_DIR:qtadvanceddocking-qt6>"
            "PATH=path_list_prepend:${VIEWER_TEST_COMPILER_RUNTIME_DIRECTORY}")
        set_property(TEST VisiCoreQtViewer.LocalFeatures PROPERTY ENVIRONMENT_MODIFICATION
            "PATH=path_list_prepend:$<TARGET_FILE_DIR:Qt6::Core>"
            "PATH=path_list_prepend:${VIEWER_TEST_COMPILER_RUNTIME_DIRECTORY}")
    endif()
endif()

install(TARGETS
    VisiCoreQtViewer
    qtadvanceddocking-qt6
    RUNTIME DESTINATION ${CMAKE_INSTALL_BINDIR})

qt_generate_deploy_app_script(
    TARGET VisiCoreQtViewer
    OUTPUT_SCRIPT deploy_script
    NO_COMPILER_RUNTIME
    NO_TRANSLATIONS
    NO_UNSUPPORTED_PLATFORM_ERROR)
install(SCRIPT ${deploy_script})

if(MINGW)
    get_filename_component(MINGW_RUNTIME_DIRECTORY "${CMAKE_CXX_COMPILER}" DIRECTORY)
    install(FILES
        "${MINGW_RUNTIME_DIRECTORY}/libgcc_s_seh-1.dll"
        "${MINGW_RUNTIME_DIRECTORY}/libstdc++-6.dll"
        "${MINGW_RUNTIME_DIRECTORY}/libwinpthread-1.dll"
        DESTINATION ${CMAKE_INSTALL_BINDIR})
endif()

set(VISICORE_MPV_LIBRARY "")
if(WIN32 AND NOT VISICORE_MPV_ROOT STREQUAL "")
    file(TO_CMAKE_PATH "${VISICORE_MPV_ROOT}" MPV_ROOT_PATH)
    foreach(candidate
            "${MPV_ROOT_PATH}/libmpv-2.dll"
            "${MPV_ROOT_PATH}/bin/libmpv-2.dll"
            "${MPV_ROOT_PATH}/mpv-2.dll"
            "${MPV_ROOT_PATH}/bin/mpv-2.dll")
        if(EXISTS "${candidate}")
            set(VISICORE_MPV_LIBRARY "${candidate}")
            break()
        endif()
    endforeach()
endif()

if(VISICORE_RELEASE_BUILD)
    if(VISICORE_MPV_LIBRARY STREQUAL "" OR VISICORE_MPV_RUNTIME_VERSION STREQUAL "" OR
       VISICORE_MPV_RUNTIME_SOURCE STREQUAL "" OR VISICORE_MPV_RUNTIME_SHA256 STREQUAL "" OR
       VISICORE_MPV_RUNTIME_LICENSE_FILE STREQUAL "")
        message(FATAL_ERROR "发行构建必须提供 libmpv 目录、版本、来源、SHA-256 与许可证文件。")
    endif()
    if(NOT EXISTS "${VISICORE_MPV_RUNTIME_LICENSE_FILE}")
        message(FATAL_ERROR "未找到 libmpv 许可证文件：${VISICORE_MPV_RUNTIME_LICENSE_FILE}")
    endif()
    file(SHA256 "${VISICORE_MPV_LIBRARY}" VISICORE_MPV_LIBRARY_SHA256)
    string(TOUPPER "${VISICORE_MPV_RUNTIME_SHA256}" VISICORE_MPV_RUNTIME_SHA256_UPPER)
    string(TOUPPER "${VISICORE_MPV_LIBRARY_SHA256}" VISICORE_MPV_LIBRARY_SHA256_UPPER)
    if(NOT VISICORE_MPV_LIBRARY_SHA256_UPPER STREQUAL VISICORE_MPV_RUNTIME_SHA256_UPPER)
        message(FATAL_ERROR "libmpv 运行时 SHA-256 与受控输入不匹配。")
    endif()
    file(WRITE "${CMAKE_CURRENT_BINARY_DIR}/visicore-viewer-runtime.json"
        "{\n  \"component\": \"libmpv\",\n  \"version\": \"${VISICORE_MPV_RUNTIME_VERSION}\",\n  \"source\": \"${VISICORE_MPV_RUNTIME_SOURCE}\",\n  \"sha256\": \"${VISICORE_MPV_LIBRARY_SHA256_UPPER}\"\n}\n")
endif()

if(WIN32 AND NOT VISICORE_MPV_LIBRARY STREQUAL "")
    install(FILES "${VISICORE_MPV_LIBRARY}" DESTINATION . RENAME "libmpv-2.dll")
endif()

if(VISICORE_RELEASE_BUILD)
    install(FILES "${CMAKE_CURRENT_BINARY_DIR}/visicore-viewer-runtime.json" DESTINATION .)
    install(FILES "${VISICORE_MPV_RUNTIME_LICENSE_FILE}" DESTINATION "licenses" RENAME "libmpv-LICENSE.txt")
endif()
