import { useEffect, useState } from 'react';
import { ActivityIndicator, StyleSheet, View } from 'react-native';
import { StatusBar } from 'expo-status-bar';
import { clearToken, getToken, saveToken } from './src/auth';
import { loadLanguage } from './src/i18n';
import { LinkedDevice } from './src/api';
import LoginScreen from './src/screens/LoginScreen';
import DevicesScreen from './src/screens/DevicesScreen';
import ConversationsScreen from './src/screens/ConversationsScreen';
import ConversationDetailScreen from './src/screens/ConversationDetailScreen';
import DeviceSettingsScreen from './src/screens/DeviceSettingsScreen';
import FlaggedScreen from './src/screens/FlaggedScreen';
import AccountScreen from './src/screens/AccountScreen';
import ProvisioningScreen from './src/screens/ProvisioningScreen';

type Screen =
  | { name: 'devices' }
  | { name: 'conversations'; deviceId: string; deviceName: string }
  | { name: 'conversationDetail'; conversationId: string; deviceId: string; deviceName: string }
  | { name: 'flagged'; deviceId: string; deviceName: string }
  | { name: 'settings'; device: LinkedDevice }
  | { name: 'provisioning'; device: LinkedDevice }
  | { name: 'account' };

function AuthedNavigator({ onLogout }: { onLogout: () => void }) {
  const [screen, setScreen] = useState<Screen>({ name: 'devices' });
  // Bumping this key remounts DevicesScreen so it re-fetches (e.g. after a
  // settings change) when we navigate back to it.
  const [devicesKey, setDevicesKey] = useState(0);

  if (screen.name === 'devices') {
    return (
      <DevicesScreen
        key={devicesKey}
        onLogout={onLogout}
        onOpenDevice={(d: LinkedDevice) =>
          setScreen({ name: 'conversations', deviceId: d.deviceId, deviceName: d.deviceName })
        }
        onOpenSettings={(d: LinkedDevice) => setScreen({ name: 'settings', device: d })}
        onOpenAccount={() => setScreen({ name: 'account' })}
      />
    );
  }

  if (screen.name === 'account') {
    return <AccountScreen onBack={() => setScreen({ name: 'devices' })} onLogout={onLogout} />;
  }

  if (screen.name === 'settings') {
    return (
      <DeviceSettingsScreen
        device={screen.device}
        onBack={() => {
          setDevicesKey((k) => k + 1);
          setScreen({ name: 'devices' });
        }}
        onChanged={() => setDevicesKey((k) => k + 1)}
        onLogout={onLogout}
        onOpenProvisioning={() => setScreen({ name: 'provisioning', device: screen.device })}
      />
    );
  }

  if (screen.name === 'provisioning') {
    return (
      <ProvisioningScreen
        device={screen.device}
        onBack={() => setScreen({ name: 'settings', device: screen.device })}
      />
    );
  }

  if (screen.name === 'conversations') {
    return (
      <ConversationsScreen
        deviceId={screen.deviceId}
        deviceName={screen.deviceName}
        onBack={() => setScreen({ name: 'devices' })}
        onOpenConversation={(conversationId: string) =>
          setScreen({
            name: 'conversationDetail',
            conversationId,
            deviceId: screen.deviceId,
            deviceName: screen.deviceName,
          })
        }
        onOpenFlagged={() =>
          setScreen({ name: 'flagged', deviceId: screen.deviceId, deviceName: screen.deviceName })
        }
        onLogout={onLogout}
      />
    );
  }

  if (screen.name === 'flagged') {
    return (
      <FlaggedScreen
        deviceId={screen.deviceId}
        deviceName={screen.deviceName}
        onBack={() =>
          setScreen({ name: 'conversations', deviceId: screen.deviceId, deviceName: screen.deviceName })
        }
        onOpenConversation={(conversationId: string) =>
          setScreen({
            name: 'conversationDetail',
            conversationId,
            deviceId: screen.deviceId,
            deviceName: screen.deviceName,
          })
        }
        onLogout={onLogout}
      />
    );
  }

  return (
    <ConversationDetailScreen
      conversationId={screen.conversationId}
      onBack={() =>
        setScreen({ name: 'conversations', deviceId: screen.deviceId, deviceName: screen.deviceName })
      }
      onLogout={onLogout}
    />
  );
}

export default function App() {
  const [booting, setBooting] = useState(true);
  const [signedIn, setSignedIn] = useState(false);

  // Restore the saved language and an existing session on launch. The
  // language must be read BEFORE the first render, or the parent would see
  // one frame of English before their choice takes effect.
  useEffect(() => {
    (async () => {
      await loadLanguage();
      const token = await getToken();
      setSignedIn(!!token);
      setBooting(false);
    })();
  }, []);

  async function handleLoggedIn(token: string) {
    await saveToken(token);
    setSignedIn(true);
  }

  async function handleLogout() {
    await clearToken();
    setSignedIn(false);
  }

  if (booting) {
    return (
      <View style={styles.center}>
        <ActivityIndicator size="large" color="#2c4a7a" />
        <StatusBar style="auto" />
      </View>
    );
  }

  return (
    <>
      {signedIn ? (
        <AuthedNavigator onLogout={handleLogout} />
      ) : (
        <LoginScreen onLoggedIn={handleLoggedIn} />
      )}
      <StatusBar style="auto" />
    </>
  );
}

const styles = StyleSheet.create({
  center: { flex: 1, alignItems: 'center', justifyContent: 'center', backgroundColor: '#fff' },
});
