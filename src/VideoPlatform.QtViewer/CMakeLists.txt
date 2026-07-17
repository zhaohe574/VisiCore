cmake_minimum_required(VERSION 3.24)
project(VideoPlatformQtViewer VERSION 0.1.0 LANGUAGES CXX)

set(CMAKE_INSTALL_BINDIR ".")

set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_AUTOMOC ON)
set(CMAKE_AUTOUIC ON)
set(CMAKE_AUTORCC ON)

find_package(Qt6 6.5 REQUIRED COMPONENTS Core Network Widgets OpenGLWidgets Svg)

include(CTest)

if(NOT CMAKE_SIZEOF_VOID_P EQUAL 8)
    message(FATAL_ERROR "Windows 查看客户端只支持 64 位工具链。")
endif()

include(cmake/ViewerThirdParty.cmake)

qt_add_executable(VideoPlatformQtViewer WIN32
    src/main.cpp
    src/crash_reporter.h
    src/crash_reporter.cpp
    src/models.h
    src/viewer_logic.h
    src/viewer_logic.cpp
    src/apiclient.h
    src/apiclient.cpp
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

qt_add_resources(VideoPlatformQtViewer videoplatform_lucide_icons
    PREFIX "/icons/lucide"
    BASE "${lucide_SOURCE_DIR}/icons"
    FILES ${VIDEOPLATFORM_LUCIDE_ICON_FILES})

set_source_files_properties(src/videoplatform_dark_theme.json
    PROPERTIES QT_RESOURCE_ALIAS "videoplatform-dark.json")
qt_add_resources(VideoPlatformQtViewer videoplatform_theme
    PREFIX "/themes"
    FILES src/videoplatform_dark_theme.json)

target_link_libraries(VideoPlatformQtViewer PRIVATE
    Qt6::Core
    Qt6::Network
    Qt6::Widgets
    Qt6::OpenGLWidgets
    Qt6::Svg
    qlementine
    ads::qtadvanceddocking-qt6)

target_compile_definitions(VideoPlatformQtViewer PRIVATE
    QT_NO_CAST_FROM_ASCII
    QT_NO_CAST_TO_ASCII
    VIDEOPLATFORM_QLEMENTINE_VERSION="${VIDEOPLATFORM_QLEMENTINE_VERSION}"
    VIDEOPLATFORM_QT_ADS_VERSION="${VIDEOPLATFORM_QT_ADS_VERSION}"
    VIDEOPLATFORM_LUCIDE_VERSION="${VIDEOPLATFORM_LUCIDE_VERSION}"
    NOMINMAX
    WIN32_LEAN_AND_MEAN)

if(MSVC)
    target_compile_options(VideoPlatformQtViewer PRIVATE /W4 /permissive- /utf-8)
else()
    target_compile_options(VideoPlatformQtViewer PRIVATE -Wall -Wextra -Wpedantic)
endif()

if(BUILD_TESTING)
    find_package(Qt6 6.5 REQUIRED COMPONENTS Test)

    qt_add_executable(VideoPlatformQtViewerLogicTests
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

    target_include_directories(VideoPlatformQtViewerLogicTests PRIVATE src)

    target_link_libraries(VideoPlatformQtViewerLogicTests PRIVATE
        Qt6::Core
        Qt6::Widgets
        Qt6::Test)

    target_compile_definitions(VideoPlatformQtViewerLogicTests PRIVATE
        QT_NO_CAST_FROM_ASCII
        QT_NO_CAST_TO_ASCII
        NOMINMAX
        WIN32_LEAN_AND_MEAN)

    if(MSVC)
        target_compile_options(VideoPlatformQtViewerLogicTests PRIVATE /W4 /permissive- /utf-8)
    else()
        target_compile_options(VideoPlatformQtViewerLogicTests PRIVATE -Wall -Wextra -Wpedantic)
    endif()

    add_test(NAME VideoPlatformQtViewer.Logic COMMAND VideoPlatformQtViewerLogicTests)
    set_property(TEST VideoPlatformQtViewer.Logic PROPERTY ENVIRONMENT "QT_QPA_PLATFORM=offscreen")

    qt_add_executable(VideoPlatformQtViewerDockTests
        tests/dock_layout_controller_tests.cpp
        src/viewer_ui_types.h
        src/viewer_ui_settings.h
        src/viewer_ui_settings.cpp
        src/viewer_startup_state.h
        src/viewer_startup_state.cpp
        src/dock_layout_controller.h
        src/dock_layout_controller.cpp)

    target_include_directories(VideoPlatformQtViewerDockTests PRIVATE src)

    target_link_libraries(VideoPlatformQtViewerDockTests PRIVATE
        Qt6::Core
        Qt6::Widgets
        Qt6::Test
        ads::qtadvanceddocking-qt6)

    target_compile_definitions(VideoPlatformQtViewerDockTests PRIVATE
        QT_NO_CAST_FROM_ASCII
        QT_NO_CAST_TO_ASCII
        NOMINMAX
        WIN32_LEAN_AND_MEAN)

    if(MSVC)
        target_compile_options(VideoPlatformQtViewerDockTests PRIVATE /W4 /permissive- /utf-8)
    else()
        target_compile_options(VideoPlatformQtViewerDockTests PRIVATE -Wall -Wextra -Wpedantic)
    endif()

    add_test(NAME VideoPlatformQtViewer.DockLayout COMMAND VideoPlatformQtViewerDockTests)
    set_property(TEST VideoPlatformQtViewer.DockLayout PROPERTY ENVIRONMENT "QT_QPA_PLATFORM=offscreen")

    qt_add_executable(VideoPlatformQtViewerTitleBarTests
        tests/window_title_bar_tests.cpp
        src/icon_provider.h
        src/icon_provider.cpp
        src/theme_manager.h
        src/theme_manager.cpp
        src/window_title_bar.h
        src/window_title_bar.cpp
        src/viewer_ui_types.h)

    target_include_directories(VideoPlatformQtViewerTitleBarTests PRIVATE src)

    target_link_libraries(VideoPlatformQtViewerTitleBarTests PRIVATE
        Qt6::Core
        Qt6::Widgets
        Qt6::Svg
        Qt6::Test
        qlementine)

    target_compile_definitions(VideoPlatformQtViewerTitleBarTests PRIVATE
        QT_NO_CAST_FROM_ASCII
        QT_NO_CAST_TO_ASCII
        NOMINMAX
        WIN32_LEAN_AND_MEAN)

    if(MSVC)
        target_compile_options(VideoPlatformQtViewerTitleBarTests PRIVATE /W4 /permissive- /utf-8)
    else()
        target_compile_options(VideoPlatformQtViewerTitleBarTests PRIVATE -Wall -Wextra -Wpedantic)
    endif()

    add_test(NAME VideoPlatformQtViewer.TitleBar COMMAND VideoPlatformQtViewerTitleBarTests)
    set_property(TEST VideoPlatformQtViewer.TitleBar PROPERTY ENVIRONMENT "QT_QPA_PLATFORM=offscreen")

    qt_add_executable(VideoPlatformQtViewerControllerTests
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

    target_include_directories(VideoPlatformQtViewerControllerTests PRIVATE src)

    target_link_libraries(VideoPlatformQtViewerControllerTests PRIVATE
        Qt6::Core
        Qt6::Test)

    target_compile_definitions(VideoPlatformQtViewerControllerTests PRIVATE
        QT_NO_CAST_FROM_ASCII
        QT_NO_CAST_TO_ASCII
        NOMINMAX
        WIN32_LEAN_AND_MEAN)

    if(MSVC)
        target_compile_options(VideoPlatformQtViewerControllerTests PRIVATE /W4 /permissive- /utf-8)
    else()
        target_compile_options(VideoPlatformQtViewerControllerTests PRIVATE -Wall -Wextra -Wpedantic)
    endif()

    add_test(NAME VideoPlatformQtViewer.Controllers COMMAND VideoPlatformQtViewerControllerTests)
    set_property(TEST VideoPlatformQtViewer.Controllers PROPERTY ENVIRONMENT "QT_QPA_PLATFORM=offscreen")

    qt_add_executable(VideoPlatformQtViewerInteractionTests
        tests/main_window_interaction_tests.cpp
        src/crash_reporter.h
        src/crash_reporter.cpp
        src/models.h
        src/viewer_logic.h
        src/viewer_logic.cpp
        src/apiclient.h
        src/apiclient.cpp
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

    qt_add_resources(VideoPlatformQtViewerInteractionTests videoplatform_interaction_lucide_icons
        PREFIX "/icons/lucide"
        BASE "${lucide_SOURCE_DIR}/icons"
        FILES ${VIDEOPLATFORM_LUCIDE_ICON_FILES})
    qt_add_resources(VideoPlatformQtViewerInteractionTests videoplatform_interaction_theme
        PREFIX "/themes"
        FILES src/videoplatform_dark_theme.json)

    target_include_directories(VideoPlatformQtViewerInteractionTests PRIVATE src)

    target_link_libraries(VideoPlatformQtViewerInteractionTests PRIVATE
        Qt6::Core
        Qt6::Network
        Qt6::Widgets
        Qt6::OpenGLWidgets
        Qt6::Svg
        Qt6::Test
        qlementine
        ads::qtadvanceddocking-qt6)

    target_compile_definitions(VideoPlatformQtViewerInteractionTests PRIVATE
        QT_NO_CAST_FROM_ASCII
        QT_NO_CAST_TO_ASCII
        NOMINMAX
        WIN32_LEAN_AND_MEAN)

    if(MSVC)
        target_compile_options(VideoPlatformQtViewerInteractionTests PRIVATE /W4 /permissive- /utf-8)
    else()
        target_compile_options(VideoPlatformQtViewerInteractionTests PRIVATE -Wall -Wextra -Wpedantic)
    endif()

    add_test(NAME VideoPlatformQtViewer.MainWindowInteraction COMMAND VideoPlatformQtViewerInteractionTests)
    set_property(TEST VideoPlatformQtViewer.MainWindowInteraction PROPERTY ENVIRONMENT "QT_QPA_PLATFORM=offscreen")

    if(WIN32)
        get_filename_component(VIEWER_TEST_COMPILER_RUNTIME_DIRECTORY "${CMAKE_CXX_COMPILER}" DIRECTORY)
        set_property(TEST VideoPlatformQtViewer.Logic PROPERTY ENVIRONMENT_MODIFICATION
            "PATH=path_list_prepend:$<TARGET_FILE_DIR:Qt6::Core>"
            "PATH=path_list_prepend:${VIEWER_TEST_COMPILER_RUNTIME_DIRECTORY}")
        set_property(TEST VideoPlatformQtViewer.DockLayout PROPERTY ENVIRONMENT_MODIFICATION
            "PATH=path_list_prepend:$<TARGET_FILE_DIR:Qt6::Core>"
            "PATH=path_list_prepend:$<TARGET_FILE_DIR:qtadvanceddocking-qt6>"
            "PATH=path_list_prepend:${VIEWER_TEST_COMPILER_RUNTIME_DIRECTORY}")
        set_property(TEST VideoPlatformQtViewer.TitleBar PROPERTY ENVIRONMENT_MODIFICATION
            "PATH=path_list_prepend:$<TARGET_FILE_DIR:Qt6::Core>"
            "PATH=path_list_prepend:${VIEWER_TEST_COMPILER_RUNTIME_DIRECTORY}")
        set_property(TEST VideoPlatformQtViewer.Controllers PROPERTY ENVIRONMENT_MODIFICATION
            "PATH=path_list_prepend:$<TARGET_FILE_DIR:Qt6::Core>"
            "PATH=path_list_prepend:${VIEWER_TEST_COMPILER_RUNTIME_DIRECTORY}")
        set_property(TEST VideoPlatformQtViewer.MainWindowInteraction PROPERTY ENVIRONMENT_MODIFICATION
            "PATH=path_list_prepend:$<TARGET_FILE_DIR:Qt6::Core>"
            "PATH=path_list_prepend:$<TARGET_FILE_DIR:qtadvanceddocking-qt6>"
            "PATH=path_list_prepend:${VIEWER_TEST_COMPILER_RUNTIME_DIRECTORY}")
    endif()
endif()

install(TARGETS
    VideoPlatformQtViewer
    qtadvanceddocking-qt6
    RUNTIME DESTINATION ${CMAKE_INSTALL_BINDIR})

qt_generate_deploy_app_script(
    TARGET VideoPlatformQtViewer
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

if(WIN32 AND DEFINED ENV{MPV_ROOT})
    file(TO_CMAKE_PATH "$ENV{MPV_ROOT}" MPV_ROOT_PATH)
    install(FILES
        "${MPV_ROOT_PATH}/bin/mpv-2.dll"
        "${MPV_ROOT_PATH}/bin/libmpv-2.dll"
        "${MPV_ROOT_PATH}/mpv-2.dll"
        "${MPV_ROOT_PATH}/libmpv-2.dll"
        DESTINATION .
        OPTIONAL)
endif()
