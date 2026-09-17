import 'package:flutter/material.dart';

/// 现代化监控控制台专用主题设计
class AppTheme {
  static const primaryColor = Color(0xFF0284C7); // 电光蓝
  static const accentCyan = Color(0xFF38BDF8);   // 亮青色
  static const onlineGreen = Color(0xFF10B981);  // 在线翡翠绿
  static const warningAmber = Color(0xFFF59E0B); // 告警琥珀橙
  static const offlineGrey = Color(0xFF64748B);  // 离线蓝灰

  // 深色控制台基色
  static const darkBg = Color(0xFF0B0F19);
  static const darkSurface = Color(0xFF151D2E);
  static const darkSurfaceElevated = Color(0xFF1E293B);
  static const darkBorder = Color(0xFF26334D);

  // 语义别名
  static const accentColor = primaryColor;
  static const scaffoldBg = darkBg;
  static const cardBg = darkSurface;
  static const surfaceBg = darkSurfaceElevated;
  static const panelBorder = darkBorder;

  static ThemeData get darkTheme {
    return ThemeData(
      brightness: Brightness.dark,
      scaffoldBackgroundColor: darkBg,
      primaryColor: primaryColor,
      colorScheme: const ColorScheme.dark(
        primary: primaryColor,
        secondary: accentCyan,
        surface: darkSurface,
      ),
      appBarTheme: const AppBarTheme(
        backgroundColor: darkSurface,
        elevation: 0,
        centerTitle: false,
        titleTextStyle: TextStyle(
          color: Colors.white,
          fontSize: 17,
          fontWeight: FontWeight.w600,
        ),
      ),
      cardTheme: CardThemeData(
        color: darkSurface,
        elevation: 0,
        shape: RoundedRectangleBorder(
          borderRadius: BorderRadius.circular(14),
          side: const BorderSide(color: darkBorder, width: 1),
        ),
      ),
      dividerColor: darkBorder,
      useMaterial3: true,
    );
  }

  static ThemeData get lightTheme {
    return ThemeData(
      brightness: Brightness.light,
      primaryColor: primaryColor,
      scaffoldBackgroundColor: const Color(0xFFF8FAFC),
      colorScheme: ColorScheme.fromSeed(
        seedColor: primaryColor,
        brightness: Brightness.light,
      ),
      appBarTheme: const AppBarTheme(
        backgroundColor: Colors.white,
        elevation: 0.5,
        centerTitle: false,
        titleTextStyle: TextStyle(
          color: Color(0xFF0F172A),
          fontSize: 17,
          fontWeight: FontWeight.w600,
        ),
      ),
      cardTheme: CardThemeData(
        color: Colors.white,
        elevation: 0.5,
        shape: RoundedRectangleBorder(
          borderRadius: BorderRadius.circular(14),
          side: const BorderSide(color: Color(0xFFE2E8F0), width: 1),
        ),
      ),
      dividerColor: const Color(0xFFE2E8F0),
      useMaterial3: true,
    );
  }
}
