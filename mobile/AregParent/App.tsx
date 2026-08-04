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
import StoryPlaysScreen from './src/screens/StoryPlaysScreen';
import StoryLibraryScreen from './src/screens/StoryLibraryScreen';
import MusicScreen from './src/screens/MusicScreen';
import StoryRequestScreen from './src/screens/StoryRequestScreen';
import ActivityScreen from './src/screens/ActivityScreen';

type Screen =
  | { name: 'devices' }
  | { name: 'conversations'; deviceId: string; deviceName: string }
  | { name: 'conversationDetail'; conversationId: string; deviceId: string; deviceName: string }
  | { name: 'flagged'; deviceId: string; deviceName: string }
  | { name: 'settings'; device: LinkedDevice }
  | { name: 'provisioning'; device: LinkedDevice }
  // Per-toy: stories play from the toy's own memory, and the library's listen
  // counts are scoped to one toy.
  | { name: 'plays'; deviceId: string; deviceName: string }
  | { name: 'library'; deviceId: string; deviceName: string }
  // Account-wide: music is the same for every toy, and requests and the
  // activity feed belong to the parent, not to a toy.
  | { name: 'music' }
  | { name: 'request' }
  | { name: 'activity' }
  | { name: 'account' };

function AuthedNavigator({ onLogout }: { onLogout: () => void }) {
  const [screen, setScreen] = useState<Screen>({ name: 'devices' });
  // Bumping this key remounts DevicesScreen so it re-fetches (e.g. after a
  // settings change) when we navigate back to it.
  const [devicesKey, setDevicesKey] = useState(0);
  // Kept so the activity feed can turn a toy id into a name on its first
  // paint instead of showing a raw id.
  const [devices, setDevices] = useState<LinkedDevice[]>([]);

  if (screen.name === 'devices') {
    return (
      <DevicesScreen
        key={devicesKey}
        onLogout={onLogout}
        onLoaded={setDevices}
        onOpenDevice={(d: LinkedDevice) =>
          setScreen({ name: 'conversations', deviceId: d.deviceId, deviceName: d.deviceName })
        }
        onOpenSettings={(d: LinkedDevice) => setScreen({ name: 'settings', device: d })}
        onOpenPlays={(d: LinkedDevice) =>
          setScreen({ name: 'plays', deviceId: d.deviceId, deviceName: d.deviceName })
        }
        onOpenLibrary={(d: LinkedDevice) =>
          setScreen({ name: 'library', deviceId: d.deviceId, deviceName: d.deviceName })
        }
        onOpenAccount={() => setScreen({ name: 'account' })}
      />
    );
  }

  if (screen.name === 'account') {
    return (
      <AccountScreen
        onBack={() => setScreen({ name: 'devices' })}
        onLogout={onLogout}
        onOpenMusic={() => setScreen({ name: 'music' })}
        onOpenRequest={() => setScreen({ name: 'request' })}
        onOpenActivity={() => setScreen({ name: 'activity' })}
      />
    );
  }

  if (screen.name === 'plays') {
    return (
      <StoryPlaysScreen
        deviceId={screen.deviceId}
        deviceName={screen.deviceName}
        onBack={() => setScreen({ name: 'devices' })}
        onLogout={onLogout}
      />
    );
  }

  if (screen.name === 'library') {
    return (
      <StoryLibraryScreen
        deviceId={screen.deviceId}
        deviceName={screen.deviceName}
        onBack={() => setScreen({ name: 'devices' })}
        onLogout={onLogout}
      />
    );
  }

  if (screen.name === 'music') {
    return <MusicScreen onBack={() => setScreen({ name: 'account' })} onLogout={onLogout} />;
  }

  if (screen.name === 'request') {
    return <StoryRequestScreen onBack={() => setScreen({ name: 'account' })} onLogout={onLogout} />;
  }

  if (screen.name === 'activity') {
    return (
      <ActivityScreen
        devices={devices}
        onBack={() => setScreen({ name: 'account' })}
        onLogout={onLogout}
      />
    );
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
