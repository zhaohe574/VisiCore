import 'package:flutter_test/flutter_test.dart';
import 'package:shared_preferences/shared_preferences.dart';
import 'package:visicore_mobile/core/network/auth_service.dart';
import 'package:visicore_mobile/main.dart';

void main() {
  TestWidgetsFlutterBinding.ensureInitialized();

  testWidgets('App launches and renders pairing page smoke test', (WidgetTester tester) async {
    SharedPreferences.setMockInitialValues({});
    await AuthService.instance.init();

    await tester.pumpWidget(const VisiCoreMobileApp());
    await tester.pumpAndSettle();

    expect(find.text('视枢 VisiCore 登录'), findsOneWidget);
    expect(find.text('点击扫一扫 (扫描电脑端二维码)'), findsOneWidget);
  });
}
